# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The API cannot run without its two local dependencies. Start them first, **from `src/`**
(the WireMock volume bind is relative to that directory):

```bash
cd src && docker compose up -d
```

That brings up DynamoDB Local on `:8000`, creates the `Payments` table, and starts the
WireMock acquiring-bank simulator on `:8080`. Add `--profile api` to also build and run the
API container on `:5067` (don't combine it with `dotnet run`, they share the port).

```bash
dotnet build PaymentGatewayApi.slnx                  # from repo root
dotnet run --project src/PaymentGatewayApi.csproj    # http://localhost:5067, Swagger UI at /swagger (Development only)
docker build -f src/Dockerfile -t payment-gateway-api .   # container image, from repo root
```

`dotnet run` sets the working directory to `src/`, which is what makes the Development
`z-logs/` output path resolve. `/health/live` answers when the process is up, `/health/ready`
also checks that the payments table is reachable. The OpenAPI document (`/openapi/v1.json`,
from `Microsoft.AspNetCore.OpenApi`) and Swagger UI are only mapped in Development.

The container image is `aspnet:10.0-noble-chiseled` running as its non-root user: it has no
shell or curl, so there is no `HEALTHCHECK`; probe the health endpoints over HTTP.

### Tests

Unit tests live in `tests/PaymentGatewayApiUnitTests` (xUnit v3 + Moq, loggers via
`NullLogger<T>.Instance`), with folders mirroring `src/`. `global.json` opts into the
Microsoft.Testing.Platform `dotnet test` mode, so run them with:

```bash
dotnet test --project tests/PaymentGatewayApiUnitTests
dotnet run --project tests/PaymentGatewayApiUnitTests   # equivalent, xUnit v3 projects are executables
```

Target the test project explicitly: a bare `dotnet test` (or `--solution`) fails because the
web project's `Microsoft.NET.Test.Sdk` reference makes it look like a VSTest project.

`BankClient` is unit-tested through a `StubHttpMessageHandler`. `PaymentsRepository` is only
mocked in unit tests; the integration tests exercise it against real DynamoDB Local.

Integration tests live in `tests/PaymentGatewayApiIntegrationTests`: the whole API in process
through `WebApplicationFactory<Program>` (environment `Testing`), DynamoDB Local started by
Testcontainers (so Docker must be running, nothing else), and the production bank client and
resilience pipeline in front of a `StubBank` handler that answers like the simulator unless a
test sets up its own answer for a card. Tests run in parallel, so each one uses its own card
numbers (`Cards.Unique`) and never shares stub state. They cover the money paths: no retry,
bank timeouts, the outcome write failing, a client disconnecting mid-payment, concurrent
idempotency keys, rate limiting, startup validation and no card number in any log line.

```bash
dotnet test --project tests/PaymentGatewayApiIntegrationTests   # ~10s, Docker required
``` Tests commented `// NOTE: defect` pin current, known-wrong
behaviour — update them when the defect is fixed.

End-to-end tests live in `tests/PaymentGatewayApiEndToEndTests`. They are black-box: they call
the running API over HTTP and do not reference the `src` project, so they need
the whole stack up, including the API container, which sits behind the compose `api` profile:

```bash
cd src && docker compose --profile api up -d --build    # api on :5067, same port as dotnet run
dotnet test --project tests/PaymentGatewayApiEndToEndTests
```

An assembly fixture (`ApiFixture`) polls `/health/live` for 30s before failing. The base URL
defaults to `http://localhost:5067`; override it with `E2E_BASE_URL` (e.g. to run against
`dotnet run` instead of the container). The bank client's resilience handler never retries
the `POST` (it is not idempotent), so the bank's `503` fails fast. The suite owns its request
record and reads responses as raw JSON (`JsonNode`), so it catches contract drift.

For manual checks, use the Bruno collection at `tests/bruno-collection.yml` (covers health,
get-by-id, authorized/declined/502 posts, and a direct simulator post), or the `curl` lines
written above each action in `Controllers/PaymentsController.cs`.

## Architecture

A `POST /api/payments` flows through one chain, each link behind an interface:

`PaymentsController` asks `IPaymentRequestValidator.Validate` (`Services/PaymentRequestValidator.cs`)
for every broken rule and answers `400` `ValidationProblemDetails` if there are any; otherwise
it hands the request to `IPaymentService` (`Services/PaymentService.cs`), which:

1. stores a `Payment` with status `Pending` through `IPaymentsRepository`
   (`Services/PaymentsRepository.cs`), so a card is never charged without a record; if this
   write fails, the request fails and the bank is never called;
2. calls `IBankClient.Authorize` (`Services/BankClient.cs`), a typed `HttpClient` that owns
   the bank contract and turns every answer, or its absence, into a `BankOutcome`;
3. records the outcome and the bank's authorization code with `Complete`, which only
   overwrites a payment that is still `Pending`. If that write fails, the merchant still gets
   the real outcome, and a `Critical` log line carries what reconciliation needs.

`PaymentStatus` (`Models/PaymentStatus.cs`) is the payment's lifecycle, stored by name:

- `Pending` — recorded, the bank has not answered.
- `Authorized` / `Declined` — the bank's answer.
- `Failed` — the bank answered 4xx or 503, or the circuit breaker is open: nothing was charged.
- `Unknown` — a timeout, a dropped connection, another 5xx or an unreadable body: the card
  may have been charged, so the payment needs reconciliation.

A request that fails validation never becomes a payment.

### HTTP contract

Every endpoint needs an `X-Api-Key` header (the authorization fallback policy), except
`/health/*` and the Development OpenAPI document. `ApiKeyAuthenticationHandler`
(`Security/`) maps the key to a merchant through `ApiKeys:Merchants`, which holds only
SHA-256 hashes of the keys. The local keys are `dev-key-merchant-a` and `dev-key-merchant-b`
(`appsettings.Development.json`, and the compose file for the container). A payment belongs
to the merchant that created it; another merchant's `GET` is a `404`, like a missing id.
`POST` is rate limited per merchant (`RateLimiting`, `429` with `Retry-After`).

| Outcome | Response |
| --- | --- |
| `Authorized` / `Declined` | `201` + `Location: /api/payments/{id}` + the payment |
| invalid request (validator or model binding) | `400` `ValidationProblemDetails`, errors keyed by JSON field |
| `Failed` / `Unknown` | `502` `ProblemDetails` with `paymentId` and `paymentStatus` extensions |
| `GET` of an unknown id, or another merchant's | `404` `ProblemDetails` |
| missing or unknown API key | `401` `ProblemDetails`, `WWW-Authenticate: ApiKey` |
| rate limit exceeded | `429` `ProblemDetails` with `Retry-After` |
| repeated `Idempotency-Key`, same request | the first request's response, plus `Idempotent-Replayed: true` |
| repeated `Idempotency-Key`, first still `Pending` | `409` `ProblemDetails` with `paymentId` |
| repeated `Idempotency-Key`, different request | `422` `ProblemDetails` |
| unhandled exception | `500` `ProblemDetails` (`GlobalExceptionHandler`), never the exception message |

Amounts are integers in the currency's minor unit (`1050` is £10.50), currencies come from
`Payments:SupportedCurrencies`, and statuses serialize as names. `PaymentRequest` is a
record whose `ToString` masks the card number and CVV.

### Models come in pairs, deliberately

`PaymentRequest` / `PaymentResponse` are this gateway's own API shape, `Payment` is its
domain record, and `BankRequest` / `BankResponse` model the acquiring bank's contract with
`snake_case` names through `[JsonPropertyName]` (`card_number`, `expiry_date`,
`authorization_code`). Keep them separate: collapsing them couples our public API to the
bank's, or our storage to either.

### Persistence

DynamoDB table `Payments`, hash key `PaymentId`. Each item has one attribute per `Payment`
field (`MerchantId`, `Status` as its name, timestamps as ISO 8601, `AuthorizationCode` only
once authorized); only the last four card digits are ever stored. The mapping lives in
`PaymentsRepository.ToItem` / `FromItem`.

Table `PaymentIdempotencyKeys`, hash key `IdempotencyKey` = `"{MerchantId}#{Key}"`, holds
`PaymentId`, a `RequestFingerprint` and `ExpiresAt` (epoch seconds, the TTL attribute;
`Payments:IdempotencyKeyRetention`, 24h). With an `Idempotency-Key`, the pending payment and
the key are written in one `TransactWriteItems`, so of two concurrent requests with the same
key exactly one reaches the bank. The fingerprint hashes the last four digits, expiry,
currency and amount: never the CVV or the full card number, which PCI DSS forbids keeping
under an unkeyed hash. The compose init script creates both tables; after pulling a schema
change, recreate local data with `docker compose down` then `up`.

### Wiring

All DI registration lives in `Program.cs`. The repository and the validator are stateless
singletons, as is the `IAmazonDynamoDB` client registered through
`AddAWSService<IAmazonDynamoDB>()`. `BankClient` is a typed `HttpClient` (transient) whose
resilience handler has retries disabled, and `PaymentService` is scoped so it never captures it. New services follow the existing pattern — interface
and implementation in the same file under `Services/` — and get registered there.

Middleware order: `CorrelationIdMiddleware`, `UseSerilogRequestLogging`, `UseExceptionHandler`
(with `GlobalExceptionHandler` and `AddProblemDetails`), `UseStatusCodePages` so bodiless errors
get a `ProblemDetails` too, Swagger UI (Development) or HSTS + HTTPS redirection (elsewhere),
then authentication, authorization, the rate limiter and a `MerchantId` log-context push.

## Bank simulator behaviour

The WireMock mappings in `src/Simulators/wiremock/mappings/` select a response from the
**last digit of the card number**. This is how you steer any manual test:

| Card ends in | Simulator response | Gateway result |
| --- | --- | --- |
| odd digit | `200` `authorized: true` | `201` `Authorized` |
| even digit (not 0) | `200` `authorized: false` | `201` `Declined` |
| `0` | `503` | `502` `Failed` |
| any missing field | `400` | `502` `Failed` |

The three `200`/`503` stubs also require `expiry_date` in the bank's `MM/yyyy` format, so a
malformed expiry falls through like any other unmatched request.
Anything else hits the catch-all `default.json` (`400`). It carries `"priority": 10` so it never
shadows the specific stubs; WireMock breaks equal-priority ties by load order.

## Conventions

- **The build enforces the style.** `Directory.Build.props` turns on `latest-recommended`
  analyzers, code-style enforcement and warnings-as-errors; `.editorconfig` holds the rules.
  Run `dotnet format PaymentGatewayApi.slnx` before committing. Package versions live only in
  `Directory.Packages.props` (central package management).
- **Explicit `using` directives** — `ImplicitUsings` is disabled, so every file declares
  its own, System first; unused ones fail the build (IDE0005). `Nullable` is enabled.
- **`this.` prefix** on all field access and only there (IDE0009).
- **Logging goes through `[LoggerMessage]`** partial methods at the bottom of each class
  (`CA1848` rejects `logger.LogX(...)` calls). Templates follow one shape:
  `"MethodName, PaymentId={PaymentId}, Status={Status}"`, with `Status` values like
  `"Started"` / `"Completed"` / `"Failed"`. `CorrelationIdMiddleware` pushes `CorrelationId`
  (the caller's `X-Correlation-ID` if it matches `^[A-Za-z0-9-]{1,64}$`, a new GUID otherwise,
  echoed on the response and in every `ProblemDetails`), and Serilog adds `TraceId`/`SpanId`,
  so never pass them by hand. Never log a `PaymentRequest`'s card number or CVV.
- **Serilog is configured from the `Serilog` section of `appsettings*.json`.** The base file
  writes compact JSON to the console (what containers collect); `appsettings.Development.json`
  swaps in a readable console format and adds files under `src/z-logs/` (gitignored).
  `UseSerilogRequestLogging` writes one summary line per request.
- **Settings bind to validated options classes** in `Configuration/` (`Bank`, `Payments`,
  `Dynamo`, `ApiKeys`, `RateLimiting`), checked at startup. Local-only values (the simulator URL, `AWS:ServiceURL`
  pointed at DynamoDB Local) live in `appsettings.Development.json`; the compose file sets
  them for the container through environment variables.
