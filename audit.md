# Payment Gateway API: audit for a senior .NET reviewer

**Scope:** the whole repository as published on branch `claude` at `ccaacc4`: source, tests, Docker/compose, config, docs and git history.
**Question:** what has to change so that a senior .NET reviewer concludes the author builds high-quality software?
**Method:** I read every tracked file and ran the build, the unit tests, the vulnerable-package scan and the outdated-package scan. I did not change any existing code. The only new file is this report.

| Check | Result |
| --- | --- |
| `dotnet build PaymentGatewayApi.slnx` (SDK 10.0.112) | ✅ 0 warnings, 0 errors (but no analyzers or warnings-as-errors are configured) |
| `dotnet test --project tests/PaymentGatewayApiUnitTests` | ✅ 61/61 passed in 1.3 s |
| `dotnet list package --vulnerable --include-transitive` | ✅ no known vulnerabilities |
| `dotnet list package --outdated` | ⚠️ `AWSSDK.DynamoDBv2` 4.0.101.2 → 4.0.104, `Moq` 4.20.69 → 4.20.72 |
| E2E suite | Not run. It needs the Docker stack; I only reviewed its code. |

---

## 1. Verdict

The repo already shows good habits, and a reviewer will notice them:

- The layers sit behind interfaces.
- `Nullable` is enabled.
- The public API models are kept apart from the bank's contract models.
- Card data is truncated to the last four digits before it is stored.
- Logging is structured.
- There is a reproducible local stack (DynamoDB Local plus a WireMock bank).
- There is a unit suite that is fast and readable.
- There is a black-box E2E suite.
- Known defects are recorded honestly in tests.

A senior reviewer will judge a **payment** gateway on something else first: **does money move correctly when things go wrong?** On that question the code fails in several concrete ways. It can charge a card twice. It can charge a card and lose the record. It sends the bank a badly formatted expiry date. It discards the authorization code. It reports a bank outage as the client's fault. Next to those problems, a reviewer also sees:

- ASP.NET Core features rebuilt by hand, or skipped: Options, typed `HttpClient`, `ProblemDetails`, health checks, `TimeProvider`, `CancellationToken`.
- An API contract that is inconsistent between endpoints.
- Repo hygiene that reads as unfinished: TODOs, commented-out code, "." commit messages, a placeholder README, no CI.

**The fastest route to a convincing repo:**
1. Fix the money-correctness issues (§2).
2. Make the HTTP contract honest (§3).
3. Replace the hand-rolled plumbing with the framework's own features (§4).
4. Add CI plus in-process integration tests, so that quality is *demonstrated*, not claimed (§6, §7).

Severity legend: 🔴 critical (a reviewer stops reading) · 🟠 high · 🟡 medium · ⚪ polish.

---

## 2. Payment correctness and resilience

### 🔴 2.1 POST to the bank is retried automatically, so one payment can be charged twice
`src/Program.cs:23-27`

```csharp
builder.Services.ConfigureHttpClientDefaults(http => { http.AddStandardResilienceHandler(); });
```

The standard resilience handler retries **every** HTTP method on 5xx, 408, 429 and timeouts. `POST /payments` to an acquiring bank is not idempotent. If the bank authorizes the payment but the response is lost or slow, the gateway sends it again. The E2E suite shows this happening: `PostPayment_CardEndingZero_InternalError` is slow *because* the POST is retried. The README even says "http retries" are omitted, which contradicts the code.

**Change:**
- Scope the resilience handler to a single typed bank client (see 4.2), not to `ConfigureHttpClientDefaults`.
- Turn retries off for unsafe methods: `options.Retry.DisableForUnsafeHttpMethods()`.
- Alternatively, retry only if the bank supports an idempotency key, and send one derived from `PaymentId`.
- Keep the attempt and total timeouts plus the circuit breaker.

### 🔴 2.2 The payment is persisted only *after* the bank call, so a failed write loses a charged payment
`src/Services/BankService.cs:35-41`

The order today is: call the bank, get `Authorized`, then call `paymentRepo.Add(...)`. If DynamoDB throws at that point:
- the card has been charged,
- no record exists,
- `ErrorMiddleware` returns a 500 to the merchant,
- the merchant will probably retry and charge the card again.

When the bank call fails (non-2xx, timeout or exception), nothing is stored at all, so there is no audit trail.

**Change:** write the payment first with status `Pending`. Call the bank. Then update it to `Authorized`, `Declined` or `Failed`/`Unknown`. For a timeout, record the uncertain outcome explicitly; a reconciliation step would resolve it later (you can document this without building it). This one change shows more payments maturity than anything else in the repo.

### 🔴 2.3 No idempotency for merchant requests
`src/Controllers/PaymentController.cs:57-84`

Every POST creates a new `PaymentId`. When a merchant retries after a network blip, a second charge is created. Payment APIs (Stripe, Checkout.com, Adyen) accept an `Idempotency-Key` header.

**Change:** accept `Idempotency-Key`. Store the key along with the result, for example as a DynamoDB item with a conditional put (`attribute_not_exists`). Replay the stored response on a duplicate. Return `409`/`422` when the same key arrives with a different body.

### 🔴 2.4 The expiry date sent to the bank has the wrong format
`src/Services/BankHttpClient.cs:34-35`

```csharp
// "04/2025"
string expiry = $"{payment.ExpiryMonth}/{payment.ExpiryYear}";   // produces "4/2025"
```

The comment on the line above states the contract (`MM/yyyy`), but the code does not follow it. Nothing catches the bug, because the WireMock stubs only check that `expiry_date` *exists*.

**Change:**
- Use `$"{payment.ExpiryMonth:D2}/{payment.ExpiryYear}"`.
- Add a unit test for the mapping.
- Tighten the stubs so `expiry_date` must match `^(0[1-9]|1[0-2])/\d{4}$`. Then the simulator enforces the contract and does not just mirror the implementation.

### 🟠 2.5 The bank's authorization code is thrown away
`src/Services/BankResponseProcessor.cs:50-57`, `src/Models/Responses/PostPaymentResponse.cs`

`BankResponse.AuthorizationCode` is parsed but never mapped or stored. Capture, refund, dispute and reconciliation all need that code.

**Change:** store it on the persisted payment record. It does not have to be exposed in the public response.

### 🟠 2.6 An unparseable bank response is stored with a null hash key
`src/Services/BankResponseProcessor.cs:28-48`, `src/Services/BankService.cs:38-39`

This defect is already pinned in `BankServiceTests.ExecutePayment_BankBodyUnparseable_StillPersistsResponseWithoutPaymentId`. The error path returns a `PostPaymentResponse` with only `Status` set, and `BankService` persists it anyway. `PaymentId = null` in the key makes DynamoDB reject the write. The exception reaches `ErrorMiddleware`, which returns a 500 and leaks DynamoDB's message.

**Change:**
- The processor should return a result type such as `BankOutcome { Authorized | Declined | Failed(reason) }`, not a half-filled DTO.
- `BankService` decides what gets persisted.
- Then remove the `// NOTE: defect` test.

### 🟠 2.7 Transport exceptions are not handled (`// todo try catch`)
`src/Services/BankHttpClient.cs:48`, `src/Services/PaymentsRepository.cs:20`

The following all propagate as a 500 whose body contains the exception message:
- `HttpRequestException`,
- `TaskCanceledException`/`TimeoutRejectedException`,
- `BrokenCircuitException`,
- `AmazonDynamoDBException`.

**Change:** catch them at the bank-client boundary, log them, and convert them into a domain outcome (`BankUnavailable`). Map that outcome to `502`/`503`. Do not leave TODOs about error handling in a payments codebase: either do the work or remove the comment and open an issue.

### 🟠 2.8 An invalid expiry year crashes instead of rejecting
`src/Services/PaymentRequestValidator.cs:51`

This defect is already pinned in unit and E2E tests. `new DateTime(0, m, 1)` throws, so the response is a 500 instead of `Rejected`.

**Change:** check the range (for example `now.Year <= year <= now.Year + 20`) before building the date, or compare `(year, month)` tuples without creating a `DateTime`. Delete both "defect" tests and replace them with `Rejected` assertions.

> **About the `// NOTE: defect` tests.** Recording bugs as tests is a good engineering instinct. In a portfolio repo, though, a reviewer reads *three* documented bugs that are easy to fix and asks why they were not fixed. Fix them. If one must stay, use `[Fact(Skip = "…issue #N")]` linked to a GitHub issue, so the suite does not stay green on known-wrong behaviour.

### 🟡 2.9 Money and domain rules are underspecified
`src/Services/PaymentRequestValidator.cs:64-91`, `src/Models/Requests/PaymentRequest.cs:22`

- `Amount` is a `decimal` in major units with a rule of `>= 1`. That rejects £0.50, accepts `1.2345`, has no upper bound, and ignores zero-decimal currencies such as JPY. Gateways normally take an **integer amount in minor units** (`long amount = 1050` → £10.50). Pick one approach and document it.
- The currency check only tests `Length == 3`: `"123"` passes, and a test even pins that behaviour. Use an ISO-4217 allow-list from configuration (`GBP`, `EUR`, `USD` is enough). Normalise the case too.
- There is no Luhn check on the card number. A senior reviewer expects one, and it is about ten lines.
- `\d` in `^\d{14,19}$` and `^\d{3,4}$` matches any Unicode digit (Arabic-Indic, full-width and so on). Use `[0-9]`, and use `[GeneratedRegex]` (source-generated, allocation-free, analyzer-friendly) instead of `Regex.IsMatch` with a string pattern.

---

## 3. API contract (what the merchant sees)

### 🔴 3.1 Status codes misreport who is at fault
`src/Controllers/PaymentController.cs:70-83`

| Outcome | Returned today | What a reviewer expects |
| --- | --- | --- |
| Authorized | `200` + plain-text id | `201 Created` + `Location: /api/payments/{id}` + payment JSON |
| Declined | `400 "Declined"` | `201`/`200` + payment JSON with `status: "Declined"`. The request was valid; the issuer said no. |
| Rejected (validation) | `400 "Rejected"` (plain text) | `400` `ValidationProblemDetails` listing *which* fields failed |
| Bank down / unparseable | `400 "InternalError"` | `502 Bad Gateway` / `503` `ProblemDetails`. **Never** 4xx: the client did nothing wrong, and 4xx tells it not to retry. |
| Unhandled exception | `500` plain text with `exception.Message` | `500` `ProblemDetails` with `traceId`, and no internal message |

### 🟠 3.2 The POST does not return the payment it created
`src/Controllers/PaymentController.cs:77-83`

`PostPaymentResponse` exists and the repository builds and stores it, but POST returns only the id (when authorized) or only a status string. A declined payment is persisted, yet the merchant never learns its id, so `GET /{id}` cannot be used for it.

**Change:** return the `PostPaymentResponse` for both Authorized and Declined.

### 🟠 3.3 The endpoints disagree on types and formats
- `PostPayment` declares `ActionResult<PaymentStatus>` (`:58`) but returns strings, so the OpenAPI document is wrong. `GetHealthcheck` declares `ActionResult<string>`.
- The POST returns statuses as text (`"Declined"`). The GET returns `PostPaymentResponse.Status` as a **number** (`0`), because `JsonStringEnumConverter` is not registered. The persisted `PaymentRaw` JSON also stores the enum as a number, so reordering `PaymentStatus` would silently corrupt stored data.
- Model-binding failures, such as a missing `required` property, already return `ProblemDetails` JSON through `[ApiController]`, while validator failures return the plain string `"Rejected"`. The same endpoint has two different shapes for 400.
- `GET /api/payment/{paymentId}` accepts any string. It should be `{paymentId:guid}` with a `Guid` parameter, so malformed ids return 400/404 without a database call.
- `NotFoundObjectResult("payment not found")` returns a plain string. Use `NotFound()`, which returns ProblemDetails.
- The code uses `new OkObjectResult(...)`/`new BadRequestObjectResult(...)` instead of the `ControllerBase` helpers (`Ok`, `CreatedAtAction`, `ValidationProblem`, `Problem`). The helpers are idiomatic and shorter.

**Change:**
- Add `[Produces("application/json")]` and `[ProducesResponseType]` on every action.
- Add `builder.Services.AddProblemDetails()`.
- Configure `JsonStringEnumConverter` globally, for MVC and for the repository serializer.
- Use a plural resource name: `api/payments`.

### 🟠 3.4 The request model accepts a server-owned field
`src/Models/Requests/PaymentRequest.cs:23`

`PaymentId` sits on the inbound DTO. Swagger therefore advertises it as an input, and the controller mutates the request object to carry it (`:60`).

**Change:** remove it from the request. Pass the id as a parameter, or create a domain `Payment` from the request. Make DTOs immutable (`record` with `init`/`required`).

### 🟡 3.5 Storage is coupled to the public API
`src/Services/PaymentsRepository.cs:37`

The author (correctly) keeps the bank models apart from the API models, then persists the **public response DTO** as the storage record. Any change to the public API becomes a data migration.

**Change:** introduce a persistence record (a `PaymentEntity` with its own attributes) and map to and from it. The stored record also needs `AuthorizationCode`, `CreatedAt`, `MerchantId`/`CallerId` and `IdempotencyKey`, which have no place in the public response.

---

## 4. ASP.NET Core idioms (showing framework fluency)

### 🟠 4.1 Configuration: use the Options pattern and validate at startup
`src/Services/BankHttpClient.cs:45`, `src/Services/PaymentsRepository.cs:39,49`

```csharp
string uri = this.config["SimulatorBankEndpoint"] ?? string.Empty;
```

Injecting `IConfiguration` into services, reading magic strings, and **falling back silently to an empty URI** means a missing setting only surfaces at request time. The table name `"Payments"` is hard-coded twice.

**Change:**
```csharp
builder.Services.AddOptions<BankOptions>().BindConfiguration("Bank")
    .ValidateDataAnnotations().ValidateOnStart();
```
Apply the same pattern to `DynamoOptions { TableName }`. The README lists "configuration file validation" as omitted; it takes about five lines, so add it.

### 🟠 4.2 Use a typed `HttpClient` and keep `HttpResponseMessage` out of the abstractions
`src/Program.cs:23-27,39`, `src/Services/BankHttpClient.cs:14,46`, `src/Services/BankResponseProcessor.cs:13`

- `IHttpClientFactory.CreateClient()` with no name, plus global defaults, means *every* HttpClient in the process gets the retry policy.
- `IBankHttpClient` returns `HttpResponseMessage`, and `IBankResponseProcessor` receives it. The transport type leaks through two abstractions, and nobody disposes the response (`BankService.cs:35`).

**Change:**
```csharp
builder.Services.AddHttpClient<IBankClient, BankClient>((sp, c) =>
        c.BaseAddress = sp.GetRequiredService<IOptions<BankOptions>>().Value.BaseUrl)
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
```
`IBankClient.AuthorizeAsync(BankRequest, CancellationToken)` then returns a domain `BankOutcome`. The client owns serialization, status-code mapping and disposal. `BankResponseProcessor` either becomes a private mapping inside the client or a pure static mapper. Both are easier to test than a class that needs an `HttpResponseMessage`.

### 🟠 4.3 Pass `CancellationToken` through the whole chain
There is none anywhere: the controller, `IBankService`, `IBankHttpClient`, `IPaymentsRepository`, `PutItemAsync` and `GetItemAsync` all lack it. A disconnected client keeps consuming bank and database capacity.

**Change:** add `CancellationToken cancellationToken` to every async interface method and flow it from the action parameter, which MVC binds from `HttpContext.RequestAborted`. Reviewers look for this first.

### 🟠 4.4 Use `TimeProvider` instead of `DateTime.Now`
`src/Controllers/PaymentController.cs:65`, `src/Services/PaymentRequestValidator.cs:11`

The controller reads the local server clock (`DateTime.Now`, which depends on the time zone) and passes it into the validator, only so that the validator can be tested.

**Change:** inject `TimeProvider` into the validator (registered as `TimeProvider.System`) and use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in tests. `IsExpiryValid(payment)` then loses its extra parameter.

### 🟡 4.5 Validation design
`src/Services/PaymentRequestValidator.cs`, `src/Controllers/PaymentController.cs:63-68`

Five boolean methods, chained with `&&` in the controller:
- stop at the first failure,
- return no error detail to the caller,
- force the controller to know the list of rules,
- make the controller tests use a string `switch` to steer mocks (`PaymentControllerTests.cs:106-123`).

**Change:** one `ValidationResult Validate(PaymentRequest)` that collects every error keyed by field and returns `ValidationProblemDetails`. Either write it by hand or use FluentValidation. An `IEndpointFilter`/action filter can run it so that the action body only handles the happy path.

### 🟡 4.6 Error handling: use the built-in pipeline
`src/Middleware/ErrorMiddleware.cs:25-37`

- `exception.Message` goes straight to the client. That is information disclosure: DynamoDB and HTTP messages include hosts, table names and so on. `ErrorMiddlewareTests.InvokeAsync_NextThrows_Returns500WithMessage` asserts that the leak happens.
- The code sets `StatusCode` without checking `context.Response.HasStarted`, so it throws if streaming has begun.
- A client abort (`OperationCanceledException`) is logged as an error and answered with a 500.
- The comment `// see if can replace message with correlationId` is an open design question left in the code.

**Change:** `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()` + `AddProblemDetails()` + `app.UseExceptionHandler()`. The handler logs the exception and returns `ProblemDetails` with `traceId`/`correlationId` and a generic message. Fix the test so it asserts that the message is *not* leaked.

### 🟡 4.7 Correlation and caller context
`src/Middleware/LogMiddleware.cs:21,32`

- `CallerId` is pushed as a raw `StringValues` from an unauthenticated header. It has no length limit and is not sanitised, so it enables log injection and log bloat. It is also not a scalar; the unit test works around that with `value.ToString()` + `Contains`.
- The client-supplied `CorrelationId` is trusted without validation and never echoed back in a response header, so a merchant cannot quote it to support.
- .NET already has W3C trace context (`Activity.Current.TraceId`, the `traceparent` header). The standard header name is `X-Correlation-ID` or `traceparent`, not `CorrelationId`.

**Change:**
- Validate the header (GUID or `^[A-Za-z0-9-]{1,64}$`) and echo it on the response.
- Push `.ToString()`.
- Enrich logs with `TraceId`.
- Take the caller identity from authentication (§5.1), not from a free-text header.
- Rename the middleware to what it does, for example `CorrelationIdMiddleware`.

### 🟡 4.8 Health checks
`src/Controllers/PaymentController.cs:33-40`

`GET /api/payment` returning `"ok"` is a business route used as a health check. It proves the process is up, but not that DynamoDB or the bank can be reached.

**Change:** `builder.Services.AddHealthChecks().AddCheck<DynamoHealthCheck>("dynamodb")` and `app.MapHealthChecks("/health/live")` plus `/health/ready`. Use them in `ApiFixture`, in a compose `healthcheck:` and in the Dockerfile.

### ⚪ 4.9 Smaller idiom issues
- **OpenAPI:** .NET 9+ ships `Microsoft.AspNetCore.OpenApi` (`AddOpenApi()`/`MapOpenApi()`), and the .NET 10 templates no longer include Swashbuckle. Moving shows the author tracks the platform. `AddEndpointsApiExplorer()` is redundant with controllers.
- **Swagger is served in every environment** (`Program.cs:47-48`). Wrap it in `if (app.Environment.IsDevelopment())`.
- Add `public partial class Program;` so that `WebApplicationFactory<Program>` works (§6.1).
- **DI:** a scoped validator, response processor and bank service hold no request state. Singleton or a static pure function is fine, and saying *why* each lifetime was chosen shows intent.
- **Naming:** `BankService` coordinates validation → bank → persistence, so it is really a `PaymentService`/`PaymentProcessor`. The log template calls it `ExecuteBankPayment` while the method is `ExecutePayment`. The controller also talks to the repository directly for GET, which bypasses the service layer that POST uses.
- `Substring(request.CardNumber.Length - 4)` → `request.CardNumber[^4..]`.
- `[HttpGet()]` → `[HttpGet]`.
- `PaymentStatus.InternalError` is commented "Should never happen" (`PaymentStatus.cs:8`), yet it is returned on every bank outage. Rename it to `Failed`/`BankUnavailable` or fix the comment.
- Consider the `[LoggerMessage]` source generator for the fixed templates. It is allocation-free, and it enforces the "one template shape" convention at compile time instead of by discipline. Today the shapes already drift: `Validator.IsExpiryMonthValid` / `IsValid=` versus the documented `MethodName, PaymentId, Status`.

---

## 5. Security and compliance posture

The README says auth and HTTPS are "intentionally omitted". A reviewer accepts omissions that are *scoped* but not ones that are *unacknowledged in the code*. Show the seams.

### 🟠 5.1 No authentication, and GET exposes any payment to anyone (IDOR)
Any caller who knows or guesses a payment id can read another merchant's payment.

**Minimum change:**
- An API-key or JWT authentication handler that maps a key to a `MerchantId`.
- `[Authorize]` on the controller.
- Store `MerchantId` on the payment and filter `GET` by it, returning 404 for other merchants' payments.

This is small, and it is the difference between "demo" and "gateway".

### 🟡 5.2 Transport and abuse protection
- `UseHttpsRedirection`/HSTS for non-development environments.
- `AddRateLimiter` (built in since .NET 7) per merchant on `POST`.
- `AllowedHosts: "*"` in the base `appsettings.json`.

### 🟡 5.3 Card-data handling (PCI DSS mindset)
Truncating to the last four digits is good. To make it visible and enforced:
- Mark `CardNumber`/`Cvv` with a redaction attribute (`Microsoft.Extensions.Compliance.Redaction` + `[DataClassification]`), or at least override `ToString()` on `PaymentRequest`, so a future `{@payment}` log call cannot leak a PAN or CVV.
- Add a test that sends a full PAN through the pipeline and checks that no log event contains it. The E2E test already asserts this for the GET response, which is the right instinct.

### ⚪ 5.4 Container
`src/Dockerfile`
- Add `USER $APP_UID`: the .NET 8+ images ship a non-root `app` user, and not using it is a common review flag.
- Consider the `-chiseled`/`noble-chiseled` runtime image.
- Use `dotnet publish --no-restore`.
- Add a `HEALTHCHECK`.

---

## 6. Tests

The unit suite is clean: arrange/act/assert, xUnit v3, `NullLogger`, and folders that mirror `src/`. Where it falls short is **coverage of the risky parts** and **evidence**.

### 🟠 6.1 In-process integration tests are missing
The two layers that handle money and I/O, `BankHttpClient` and `PaymentsRepository`, are "deliberately not unit-tested". The only thing that exercises them is an E2E suite that needs `docker compose` and is not run anywhere automatically.

**Change:** add `PaymentGatewayApiIntegrationTests` built on `WebApplicationFactory<Program>`:
- Replace the bank with a stub `HttpMessageHandler`, or `WireMock.Net` in-process (the repo already has the mapping files).
- Run DynamoDB through `Testcontainers.DynamoDb`, or replace `IPaymentsRepository` for the pure HTTP-contract tests.
- Cover: status codes and body shapes per §3.1, the `MM/yyyy` expiry mapping (§2.4), **no retry on POST** (§2.1: the handler counts calls), the bank timing out, DynamoDB throwing after authorization (§2.2), idempotent replay (§2.3), and `CancellationToken` flow.

### 🟡 6.2 The E2E tests reuse the server's own models
`tests/PaymentGatewayApiEndToEndTests/PaymentGatewayApiEndToEndTests.csproj:16-19`

A "black-box" test that serializes with the server's own `PaymentRequest`/`PostPaymentResponse` cannot detect contract drift. Rename a property and both sides change together, and the test still passes.

**Change:** give the E2E project its own small request/response records, or assert on raw JSON (`JsonNode`) with explicit property names.

### 🟡 6.3 Specific test smells
- `PaymentControllerTests.PostPayment_CardNumberInvalid_SkipsRemainingChecks` (`:133-144`) has a commented-out line (`// this.SetupValidator(isValid: false);`). It relies on Moq's loose default returning `false`. Set the card check to `false` explicitly.
- `PostPayment_ValidationFails_…` uses a `switch` on method names to configure mocks. That is a sign the validator interface is shaped wrong (§4.5).
- `ErrorMiddlewareTests` asserts the leak (`Assert.Contains("boom", body)`); see §4.6.
- `IsCurrencyCodeValid_ThreeCharacters_ReturnsTrue("123")` pins a weak rule as correct.
- The E2E `Expiry` case uses `DateTime.Now.Year - 1` and `TestData` uses `DateTime.Now.Year + 1`. These are fine, but a fixed clock (`FakeTimeProvider`, §4.4) removes the time-of-year dependence from the unit layer entirely.
- No coverage is collected. Add `Microsoft.Testing.Extensions.CodeCoverage` and publish the percentage in CI. A reviewer trusts a badge more than a claim.
- Moq 4.20.69 is outdated (4.20.72). Given the 2023 SponsorLink episode, some reviewers prefer NSubstitute. That is optional, but know why you chose Moq.

---

## 7. Engineering hygiene (first impressions)

A reviewer sees these items **before** reading any C#.

### 🟠 7.1 No CI
There is no `.github/workflows`.

**Change:** add one workflow that runs `dotnet restore`, `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, the unit and integration tests with coverage, and the vulnerable-package scan. Optionally add a second job that runs `docker compose --profile api up` plus the E2E suite. Put a status badge in the README.

### 🟠 7.2 README
The current README is five lines. It contains `add later how to run / debug`, typos ("Intentional kept simple so that can demo"), and a claim that retries are omitted when they are not. Meanwhile `AGENTS.md`, written for an AI agent, is thorough. A reviewer notices the imbalance: the machine got better documentation than the human.

**Change:** a README with:
- a one-paragraph purpose and an architecture diagram (the request flow in `AGENTS.md` is ready to reuse),
- a quick start (`docker compose up`, `dotnet run`, test commands),
- an API table with example requests and responses,
- the card-number → simulator behaviour table,
- **design decisions and trade-offs**: why DynamoDB, why the separate bank models, the retry and idempotency stance, what is out of scope and what would come next.

A short "Known limitations / next steps" section turns the omissions into deliberate choices.

### 🟡 7.3 Leftovers that read as unfinished
- `Program.cs:20-21`: a stale comment ("using simple httpClient for dev purpose") directly above code that does something else; commented-out `AddHttpClient()`; three blank lines (`:28-30`); commented-out links and code at the end (`:56-59`).
- `// todo try catch`, `// todo add try catch for all possible scenarios`, `// or return bad request if missing correlationId`, `// see if can replace message with correlationId`, `// most probably not needed, date should be already valid`: resolve each one or move it to a GitHub issue.
- Commented-out XML in `PaymentGatewayApiUnitTests.csproj:10,13-15`.
- `deploy/aws/.gitkeep` with `deploy/aws/*` ignored: an empty promise. Remove it or add real IaC (CDK/Terraform for the table).
- `launchSettings.json` still has the IIS Express profile and an unused `sslPort` from the template, plus a profile named `PaymentGateway.Api` that does not match the project name.
- `PutItemResponse response = …` is assigned and never used (`PaymentsRepository.cs:39`).
- Unused `using` directives: `System.Collections.Generic`, `System.Net`, `System.Net.Http`, `System.Text.Json` and `Microsoft.AspNetCore.Http` in `PaymentController.cs`; `Serilog` in `BankHttpClient.cs` and `BankService.cs`; `PaymentGatewayApi.Services` inside its own namespace in `BankService.cs`; `System`/`System.Net` in `BankResponseProcessor.cs`; `System.Net` in `PaymentsRepository.cs`. `ImplicitUsings` is disabled on purpose, and that choice only pays off if the explicit lists stay exact. Enforce it with IDE0005 as an error.
- Inconsistent `using` order: `System.*`, then `PaymentGatewayApi.*`, then `Microsoft.*` in some files; alphabetical in the tests.

### 🟡 7.4 Build-quality guard rails
There is no `Directory.Build.props`, `.editorconfig`, `Directory.Packages.props` or `.gitattributes`.

**Change:**
- A `Directory.Build.props` with `TargetFramework`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, and optionally `GenerateDocumentationFile` for the API project. That removes the duplication across three csproj files.
- Central Package Management (`Directory.Packages.props`), which gives one place to bump versions.
- An `.editorconfig` that encodes the house style, including `this.` qualification (`dotnet_style_qualification_for_field = true:error`), so the convention is enforced by tooling and not only described in `AGENTS.md`.
- A `.gitattributes` (`* text=auto eol=lf`). Today `PaymentGatewayApiUnitTests.csproj` mixes CRLF and LF, `PaymentsRepository.cs` alone has a UTF-8 BOM, and seven files have no final newline (`README.md`, `ErrorMiddleware.cs`, `BankResponseProcessor.cs`, `BankService.cs`, `PaymentsRepository.cs`, `logconfig.json`, the unit-test csproj).
- Bump `AWSSDK.DynamoDBv2` to 4.0.104. Consider Dependabot.

### 🟡 7.5 Logging configuration
`src/Program.cs:14-16`, `src/logconfig.json`

- A second `ConfigurationBuilder` reads `logconfig.json` relative to the **working directory**. It gets no environment overrides (`logconfig.Development.json`), no environment variables and no reload.
- There is no bootstrap logger and no `try/catch/finally Log.CloseAndFlush()` around `app.Run()`. Startup failures go unlogged, and the file sink may lose buffered events on shutdown.
- In the container, logs are written to files inside an ephemeral filesystem. The console sink, the one that containers and cloud platforms collect, is "deliberately terse" and **omits CorrelationId**, which is the property that matters most in production.
- There is no `UseSerilogRequestLogging()`, so there is no single summary line per request with status and elapsed time.

**Change:** move the Serilog section into `appsettings.json` / `appsettings.Development.json` and use `builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration))`. Development gets pretty console plus file output; other environments get JSON to the console (`CompactJsonFormatter`). Add request logging and the flush.

### 🟡 7.6 Environment configuration
`appsettings.json` has `AWS:ServiceURL = http://localhost:8000`. That is a development-only value in the **base** file, so any environment that forgets to override it points at localhost.

**Change:** move it to `appsettings.Development.json`. Also document that `dotnet run` against DynamoDB Local still needs *some* AWS credentials to resolve (compose sets dummy ones for the container only).

### ⚪ 7.7 Git history
The history has several commits named `.` (`95e9870`, `555f4f2`, `7a4ed04`, `0ffd78e`) and many named `minor …`. Reviewers do read `git log`.

**Change:**
- From now on, use imperative, descriptive messages (Conventional Commits is optional) and squash-merge PRs with a meaningful title.
- Rewriting already-published history is your call. If the repo is mainly a portfolio piece, a single squash onto a clean `main` is defensible.
- Fix the heading `# CLAUDE.md` in `AGENTS.md`.
- Add a `LICENSE`.

### ⚪ 7.8 Documentation drift in `AGENTS.md`
- Line 73 names `IPostPaymentRepository`, but the code has `IPaymentsRepository`.
- Line 39 says a bare `dotnet test` fails because of "the web project's `Microsoft.NET.Test.Sdk` reference". `src/PaymentGatewayApi.csproj` has no such reference, directly or transitively.

Inaccurate docs cost a reviewer more trust than missing ones.

---

## 8. Prioritised plan

These are ordered by how much each item moves a senior reviewer's opinion per hour of work.

| # | Change | Refs | Effort |
| --- | --- | --- | --- |
| 1 | No automatic retry on bank POST; typed bank client scoped to its own resilience pipeline | 2.1, 4.2 | S |
| 2 | Fix `MM/yyyy` expiry formatting, and make WireMock enforce the format | 2.4 | XS |
| 3 | Fix the three pinned defects (year 0, null-key persist, unhandled transport errors) and delete the `NOTE: defect` tests | 2.6-2.8 | S |
| 4 | Honest HTTP contract: 201 + `Location` + body, Declined as a result not a 400, 502/503 for bank failures, `ProblemDetails`/`ValidationProblemDetails` everywhere, string enums, `{id:guid}` | 3.1-3.4 | M |
| 5 | `AddExceptionHandler` + `AddProblemDetails`; stop leaking exception messages | 4.6 | S |
| 6 | Persist `Pending` → call the bank → update; store `AuthorizationCode`; separate persistence entity | 2.2, 2.5, 3.5 | M |
| 7 | `CancellationToken` everywhere; `TimeProvider`; Options + `ValidateOnStart` | 4.1, 4.3, 4.4 | S |
| 8 | CI workflow (build with warnings as errors, format check, tests + coverage, vulnerable-package scan) + `Directory.Build.props` + `.editorconfig` + CPM | 7.1, 7.4 | S |
| 9 | `WebApplicationFactory` integration tests covering the scenarios in 6.1 | 6.1 | M |
| 10 | README rewrite with design decisions and trade-offs; remove TODOs and commented code; fix `AGENTS.md` drift | 7.2, 7.3, 7.8 | S |
| 11 | `Idempotency-Key` support | 2.3 | M |
| 12 | API-key authentication + merchant scoping on GET; rate limiting; HTTPS/HSTS outside development | 5.1, 5.2 | M |
| 13 | Health checks (live and ready), non-root container, Serilog config consolidation, console output in JSON | 4.8, 5.4, 7.5 | S |
| 14 | Validator redesign (collect all errors, Luhn, ISO-4217 allow-list, minor units, `[GeneratedRegex]` with `[0-9]`) | 2.9, 4.5 | S |

Items 1-10 are roughly two to three focused days. Once they are done, the repo shows a reviewer that the author knows where payment systems fail and knows the framework's own tools. That is the signal "builds high-quality software" depends on.
