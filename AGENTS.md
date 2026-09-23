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
dotnet run --project src/PaymentGatewayApi.csproj    # http://localhost:5067, Swagger at /swagger
docker build -f src/Dockerfile -t payment-gateway-api .   # container image, from repo root
```

`dotnet run` sets the working directory to `src/`, which is what makes `logconfig.json` and
the `z-logs/` output path resolve.

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

`BankClient` is unit-tested through a `StubHttpMessageHandler`. `PaymentsRepository` is
deliberately not unit-tested; it is only mocked through its interface. Tests commented `// NOTE: defect` pin current, known-wrong
behaviour — update them when the defect is fixed.

End-to-end tests live in `tests/PaymentGatewayApiEndToEndTests`. They are black-box: they call
the running API over HTTP (the `src` project is referenced only for its models), so they need
the whole stack up, including the API container, which sits behind the compose `api` profile:

```bash
cd src && docker compose --profile api up -d --build    # api on :5067, same port as dotnet run
dotnet test --project tests/PaymentGatewayApiEndToEndTests
```

An assembly fixture (`ApiFixture`) polls the healthcheck for 30s before failing. The base URL
defaults to `http://localhost:5067`; override it with `E2E_BASE_URL` (e.g. to run against
`dotnet run` instead of the container). The bank client's resilience handler never retries
the `POST` (it is not idempotent), so the bank's `503` fails fast. String results (`"ok"`, the payment id,
status names) come back as `text/plain`, not JSON strings.

For manual checks, use the Bruno collection at `tests/bruno-collection.yml` (covers health,
get-by-id, authorized/declined/500 posts, and a direct simulator post), or the `curl` lines
written above each action in `Controllers/PaymentController.cs`.

## Architecture

A `POST /api/payment` flows through one chain, each link behind an interface:

`PaymentController` runs the five `IPaymentRequestValidator` checks
(`Services/PaymentRequestValidator.cs`), then hands the request to `IPaymentService`
(`Services/PaymentService.cs`), which:

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

### Models come in pairs, deliberately

`PaymentRequest` / `PostPaymentResponse` are this gateway's own API shape, `Payment` is its
domain record, and `BankRequest` / `BankResponse` model the acquiring bank's contract with
`snake_case` names through `[JsonPropertyName]` (`card_number`, `expiry_date`,
`authorization_code`). Keep them separate: collapsing them couples our public API to the
bank's, or our storage to either.

### Persistence

DynamoDB table `Payments`, hash key `PaymentId`. Each item has one attribute per `Payment`
field (`Status` as its name, timestamps as ISO 8601, `AuthorizationCode` only once
authorized); only the last four card digits are ever stored. The mapping lives in
`PaymentsRepository.ToItem` / `FromItem`.

### Wiring

All DI registration lives in `Program.cs`. The repository and the validator are stateless
singletons, as is the `IAmazonDynamoDB` client registered through
`AddAWSService<IAmazonDynamoDB>()`. `BankClient` is a typed `HttpClient` (transient) whose
resilience handler has retries disabled, and `PaymentService` is scoped so it never captures it. New services follow the existing pattern — interface
and implementation in the same file under `Services/` — and get registered there.

Middleware order is `LogMiddleware` then `ErrorMiddleware`.

## Bank simulator behaviour

The WireMock mappings in `src/Simulators/wiremock/mappings/` select a response from the
**last digit of the card number**. This is how you steer any manual test:

| Card ends in | Simulator response | Gateway result |
| --- | --- | --- |
| odd digit | `200` `authorized: true` | `Authorized` |
| even digit (not 0) | `200` `authorized: false` | `Declined` |
| `0` | `503` | `Failed` |
| any missing field | `400` | `Failed` |

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
  `"Started"` / `"Completed"` / `"Failed"`. `CallerId` and `CorrelationId` are enriched
  automatically by `LogMiddleware` from the request headers (a missing `CorrelationId`
  header gets a generated GUID), so never pass them by hand.
- **Serilog is configured from `src/logconfig.json`, not `appsettings.json`.**
  Settings bind to validated options classes in `Configuration/` (`Bank`, `Dynamo`),
  checked at startup. Local-only values (the simulator URL, `AWS:ServiceURL` pointed at
  DynamoDB Local) live in `appsettings.Development.json`; the compose file sets them for
  the container through environment variables. Verbose file logs go to `src/z-logs/`
  (gitignored); the console sink is deliberately terse.
