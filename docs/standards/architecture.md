# Architecture rules (ARCH-*)

How the system is put together: boundaries, data flow, failure semantics. A change that breaks one of
these needs the rule changed first, in its own reviewed change.

Each rule: the rule, **Why**, **Check** (what a reviewer looks for). IDs are stable: never renumber or
reuse an ID; retire a rule by marking it `Retired` and saying what replaced it.

Context: this is a demo of SOLID and system design, not a production gateway. Items in the README's
"Out of scope" list are accepted trade-offs; see ARCH-017.

---

## Boundaries and models

### ARCH-001 Layering: Controllers → Services → Models
Controllers depend only on service interfaces (`IPaymentsRepository`, `IPaymentRule`, `IPaymentProcessor`).
Only services touch infrastructure clients (`IAmazonDynamoDB`, `HttpClient`). Models depend on nothing.
- **Why:** keeps HTTP, business flow and infrastructure swappable and unit-testable with mocks.
- **Check:** no `Amazon.*` or `System.Net.Http` usings in `src/Controllers`; no `Microsoft.AspNetCore.Mvc` in `src/Services` or `src/Models`.

### ARCH-002 Three model families, never shared across a boundary
- API: `PaymentRequest` in, `MerchantResponse` out.
- Bank contract: `BankRequest` / `BankResponse`, mirroring the bank's JSON.
- Storage: `PaymentDb`.

Mapping happens at the edge that owns it: `PaymentRequest → BankRequest` in `BankHttpClient`,
`BankResponse → PaymentDb` in `BankResponseProcessor`, `PaymentDb → MerchantResponse` in the controller.
- **Why:** the bank's contract, our storage format and our public API change for different reasons.
- **Check:** `BankRequest`/`BankResponse` referenced only by `BankHttpClient`, `BankResponseProcessor`, `PaymentProcessor`; no bank or storage type returned from a controller action.

### ARCH-003 Card data is never stored, logged or returned in full
Only `CardNumberLastFour` is persisted. The full card number and CVV go to the bank and nowhere else:
not to DynamoDB, not to logs, not into validation error messages, not into responses.
- **Why:** card data handling (PCI DSS); the demo still behaves as if it mattered.
- **Check:** new log calls, `PaymentDb` fields, `MerchantResponse` fields and `ValidationError` messages never include `CardNumber` or `Cvv`.

---

## Payment flow

### ARCH-004 Request flow order is fixed
`POST /api/payments`: normalize currency → idempotency lookup → validation → bank call → parse → store → respond.
A replay of a final payment returns before validation runs.
- **Why:** documented in the README "How it works"; idempotency and "nothing stored on validation failure" depend on this order.
- **Check:** diffs in `PaymentController.PostPayment` or `PaymentProcessor.ExecutePayment` that reorder these steps.

### ARCH-005 Validation is a set of independent `IPaymentRule`s
One concern per rule class in `src/Services/Validators`, registered in `Program.cs`. Every rule runs and all
errors are returned together as one validation problem. Registration order is the error order.
On any validation error the bank is not called and nothing is stored.
- **Why:** open/closed: a new check is a new class plus one registration line, nothing else changes.
- **Check:** new validation logic lives in a new `IPaymentRule`, not in the controller or processor; rules don't short-circuit each other.

### ARCH-006 Payment status semantics
- `Authorized`, `Declined`: final, come only from a well-formed bank answer.
- `Unavailable`: the payment was valid but the bank was unreachable, failed after retry, or answered outside its contract (null body, authorized without an authorization code). Retryable.
- `Unknown`: enum default only; never stored, never returned for a valid payment.

Any failure after validation passed results in `Unavailable` with `201`, not a `5xx`.
- **Why:** the merchant always gets a definite, retryable answer for a valid payment.
- **Check:** new failure paths in the bank call or parsing map to `Unavailable`; nothing assigns `Unknown`.

### ARCH-007 Idempotency on the merchant's `paymentId`
The merchant-supplied `paymentId` is the idempotency key and the DynamoDB partition key.
A stored `Authorized`/`Declined` payment is replayed without calling the bank; a stored `Unavailable` one is retried.
`PaymentsRepository.Put` uses a condition expression so a final payment is never overwritten.
- **Why:** safe merchant retries; the bank is assumed idempotent on `paymentId`.
- **Check:** the conditional put stays; no code path generates our own payment ID.

### ARCH-008 Failure handling per component
- `BankHttpClient` never throws: it logs and returns `null` (→ `Unavailable`).
- `PaymentsRepository.Put` never throws: the bank has already answered, so a storage failure is logged and the merchant still gets the bank's status.
- `PaymentsRepository.Get` throws `InvalidDataException` on a corrupt item → `500` via the exception handler.
- Anything else unexpected bubbles to `ExceptionHandler` → `500` problem details.
- **Why:** hiding an authorized payment behind a `500` is worse than a missing record (covered by monitoring).
- **Check:** new `catch` blocks match this table; no swallowed exceptions elsewhere.

### ARCH-009 No cancellation tokens in the payment path
Do not add or pass `CancellationToken`s (framework-mandated signatures like `IExceptionHandler.TryHandleAsync` excepted, and the token is not used).
- **Why:** a client disconnect must not cancel an in-flight authorization at the bank or the store of its result.
- **Check:** no new `CancellationToken` parameters or arguments. (Also an AGENTS.md ground rule.)

---

## Cross-cutting

### ARCH-010 `Program.cs` is the only composition root
All DI registrations, options binding, HTTP client and resilience setup, problem details setup and
middleware order live in `src/Program.cs`. Middleware order: `LogMiddleware` → `UseExceptionHandler` → `UseStatusCodePages` → endpoints.
- **Why:** one place to see how the app is wired; `LogMiddleware` must wrap the exception handler so `CallerId` is in scope for the error log and `traceresponse` survives the response being cleared.
- **Check:** no `services.Add*` outside `Program.cs`; middleware order unchanged unless the change is the point.

### ARCH-011 Resilience is configured on the HTTP client pipeline
Retries and timeouts for outbound calls are set with `AddResilienceHandler` (and AWS SDK client config) in `Program.cs`, never hand-rolled loops in services.
- **Why:** policy in one place, tunable without touching business code.
- **Check:** no retry loops, `Task.Delay` or manual timeouts in `src/Services`.

### ARCH-012 Configuration through validated typed options
Settings are bound to an options class (`GatewayOptions`), validated by an `IValidateOptions<T>` with `ValidateOnStart`, and injected as `IOptions<T>`. Services don't read `IConfiguration`.
- **Why:** bad config fails at startup, not on the first payment.
- **Check:** new settings get a property, a validator check and an `appsettings.json` entry.

### ARCH-013 Errors are RFC 9457 problem details
Error responses go through `this.Problem(...)`, `this.ValidationProblem()` or `IProblemDetailsService`. `instance` and `traceId` are added centrally in `Program.cs`.
- **Why:** one error shape for clients; trace ID links a response to logs.
- **Check:** no hand-built error objects or `StatusCode(4xx, new { ... })`.

### ARCH-014 Time comes from `TimeProvider`
Inject `TimeProvider`; never call `DateTime.Now/UtcNow` or `DateTimeOffset.Now/UtcNow`.
- **Why:** deterministic tests (card expiry, `CreatedAt`).
- **Check:** no `DateTime.Now/UtcNow` or `DateTimeOffset.Now/UtcNow` in `src/`. (End-to-end tests run against the real clock, so they may use it.)

### ARCH-015 Observability uses W3C trace context, not custom IDs
Tracing is OpenTelemetry; the trace ID is the correlation ID (returned as `traceresponse`, in problem details, in file logs). The caller is identified by the `CallerId` header pushed into the log context.
- **Why:** replaced a custom correlation ID (commit 029e473); standard propagation works across services.
- **Check:** no new correlation/request ID headers or log properties.

### ARCH-016 The bank simulator is the bank's contract for local and e2e runs
Bank behaviour is defined by the WireMock mappings in `src/Simulators/wiremock/mappings/` (last card digit decides the answer). A change to `BankRequest`/`BankResponse` updates the mappings and the README simulator table in the same change.
- **Why:** e2e tests and manual checks depend on it.
- **Check:** bank contract diffs come with mapping diffs.

### ARCH-017 Scope is deliberately small
The README "Out of scope" list is accepted. Reviewers don't flag those gaps (e.g. no locking on concurrent same-`paymentId` requests, no auth, unversioned `PaymentRaw`), and implementers don't fix them unless a task says so.
- **Why:** the project demonstrates design, not completeness.
- **Check:** a change touching an out-of-scope item is linked to a task that asked for it.
