## Ground rules

- **Never execute any git commands** unless the user explicitly asks for it in that request.
- **Never write or modify tests** unless the user explicitly asks for it.
- **Never add private methods** unless for tests.
- **Never add cancellation tokens** unless the user explicitly asks for it.

## Commands

The API cannot run without its two local dependencies. Start them first, **from `src/`**
(the WireMock volume bind is relative to that directory):

```bash
cd src && docker compose up -d
```

That brings up DynamoDB Local on `:8000`, creates the `PendingPayments` and `CompletedPayments`
tables, and starts the
WireMock acquiring-bank simulator on `:8080`. Add `--profile api` to also build and run the
API container on `:5067` (don't combine it with `dotnet run`, they share the port).

```bash
dotnet build PaymentGatewayApi.slnx                  # from repo root
dotnet run --project src/PaymentGatewayApi.csproj    # http://localhost:5067, Swagger at /swagger
docker build -t payment-gateway-api src/             # container image
```

`logconfig.json` is added to `builder.Configuration`, so it resolves against the content root
(`src/` under `dotnet run`). The `z-logs/` output path is still relative to the working
directory, which `dotnet run` sets to `src/`.

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

`BankHttpClient` and `PaymentsRepository` are deliberately not unit-tested; they are only
mocked through their interfaces. Tests commented `// NOTE: defect` pin current, known-wrong
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
`dotnet run` instead of the container). No resilience handler is configured on the bank
`HttpClient` (it is commented out in `Program.cs`), so the simulator's `503` is not retried.
String results (`"ok"`, rejected/error
status names) come back as `text/plain`, not JSON strings.

For manual checks, use the Bruno collection at `tests/bruno-collection.yml` (covers health,
get-by-id, authorized/declined/500 posts, and a direct simulator post), or the `curl` lines
written above each action in `Controllers/PaymentController.cs`.

## Architecture

A `POST /api/payment` flows through one chain, each link behind an interface:

`PaymentController` keeps a client-supplied `PaymentId` as-is (unvalidated; `GET` only
matches GUIDs) or assigns a new GUID when it is missing, runs all five
`IPaymentRequestValidator` checks (`Services/PaymentRequestValidator.cs`), then hands off to
`IBankService` (`Services/BankService.cs`). `BankService` first persists the payment as
`Pending` to `PendingPayments` via `IPaymentsRepository.Add` (`Services/PaymentsRepository.cs`; a failure there throws,
so the bank is never called), then calls `IBankHttpClient` (`Services/BankHttpClient.cs`, POSTs to
`payments` under `SimulatorBankBaseAddress`), passes a successful response to
`IBankResponseProcessor` (`Services/BankResponseProcessor.cs`) to map it onto a
`PostPaymentResponse`, and moves the record, with its final status, to `CompletedPayments` via
`IPaymentsRepository.Complete` (which logs rather than throws) before returning it.

`PaymentStatus` (`Models/PaymentStatus.cs`) is the contract between layers:

- `Rejected` — local validation failed; the bank was never called.
- `Declined` — the bank answered `authorized: false`.
- `InternalError` — the bank returned non-2xx, or its body could not be parsed.
- `Pending` — persisted before the bank is called; replaced by the final status once it answers.

### Models come in two pairs, deliberately

`PaymentRequest` / `PostPaymentResponse` are this gateway's own API shape.
`BankRequest` / `BankResponse` model the acquiring bank's contract and use `snake_case`
through `[JsonPropertyName]` (`card_number`, `expiry_date`, `authorization_code`). Keep
them separate — collapsing them couples our public API to the bank's.

### Persistence

Two DynamoDB tables, `PendingPayments` and `CompletedPayments`, both with hash key `PaymentId`
and the same item shape: just `PaymentId` (S), `PaymentRaw` (S) holding the JSON-serialized
`PostPaymentResponse` (including the bank's `AuthorizationCode`), and `CreatedAt` (S), the ISO 8601
UTC time the item was written to that table. `CreatedAt` is set by the repository, not the table
definition: DynamoDB only declares key attributes, so compose can't define it. Only the last four card digits
are ever stored.

`Add` writes to `PendingPayments`. `Complete` moves the payment in one `TransactWriteItems`
(put into `CompletedPayments`, delete from `PendingPayments`), so it is never in both or neither.
A row left in `PendingPayments` means the bank outcome was never recorded (in flight, or
`Complete` failed and only the error log holds the `PaymentRaw`): that table is the
reconciliation worklist. `Get` reads `CompletedPayments` first, then falls back to
`PendingPayments`.

### Wiring

All DI registration lives in `Program.cs`. Services are scoped, except `BankHttpClient`,
a typed client registered through `AddHttpClient<IBankHttpClient, BankHttpClient>()`
(transient, gets its own `HttpClient`). The repository is a singleton, as is the
`IAmazonDynamoDB` client registered through `AddAWSService<IAmazonDynamoDB>()`. New services follow the existing pattern — interface
and implementation in the same file under `Services/` — and get registered there.

Middleware order is `LogMiddleware` then `ErrorMiddleware`.

## Bank simulator behaviour

The WireMock mappings in `src/Simulators/wiremock/mappings/` select a response from the
**last digit of the card number**. This is how you steer any manual test:

| Card ends in | Simulator response | Gateway result |
| --- | --- | --- |
| odd digit | `200` `authorized: true` | `Authorized` |
| even digit (not 0) | `200` `authorized: false` | `Declined` |
| `0` | `503` | `InternalError` |
| any missing field | `400` | `InternalError` |

Anything else hits the catch-all `default.json` (`400`). It carries `"priority": 10` so it never
shadows the specific stubs; WireMock breaks equal-priority ties by load order.

## Conventions

- **Explicit `using` directives** — `ImplicitUsings` is disabled, so every file declares
  its own. `Nullable` is enabled.
- **`this.` prefix** on all field access, consistently throughout.
- **Structured logging** follows one template shape:
  `"PaymentId={PaymentId}, Operation={Operation}, Status={Status}"`, where `Operation` is
  the method name passed as an argument (never inlined in the template), with `Status` values like
  `"Started"` / `"Completed"` / `"Failed"`. `CallerId` and `CorrelationId` are enriched
  automatically by `LogMiddleware` from the request headers (a missing `CorrelationId`
  header gets a generated GUID), so never pass them by hand. The resolved `CorrelationId` is
  echoed back in a `CorrelationId` response header.
- **Serilog is configured from `src/logconfig.json`, not `appsettings.json`.**
  `appsettings.json` holds `SimulatorBankBaseAddress`, `SupportedCurrencies` (the only
  currencies `IsCurrencyCodeValid` accepts, matched case-insensitively) and the `AWS` block (pointed at
  `http://localhost:8000` for DynamoDB Local). Verbose file logs go to `src/z-logs/`
  (gitignored); the console sink is deliberately terse.
  In a container (`DOTNET_RUNNING_IN_CONTAINER=true`, set by the .NET base images)
  `logconfig.container.json` is loaded instead: `CompactJsonFormatter` to stdout only, no files.
  Keep its `MinimumLevel` in step with `logconfig.json`.
