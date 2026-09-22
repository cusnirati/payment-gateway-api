# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The API cannot run without its two local dependencies. Start them first, **from `src/`**
(the WireMock volume bind is relative to that directory):

```bash
cd src && docker compose up -d
```

That brings up DynamoDB Local on `:8000`, creates the `Payments` table, and starts the
WireMock acquiring-bank simulator on `:8080`.

```bash
dotnet build PaymentGatewayApi.slnx                  # from repo root
dotnet run --project src/PaymentGatewayApi.csproj    # http://localhost:5067, Swagger at /swagger
docker build -t payment-gateway-api src/             # container image
```

`dotnet run` sets the working directory to `src/`, which is what makes `logconfig.json` and
the `z-logs/` output path resolve.

### Tests

**There is no unit test project.** `Microsoft.NET.Test.Sdk` is referenced from the web
project and `GenerateProgramFile=false` is set, but no test files exist — adding tests
means adding a new test project and registering it in `PaymentGatewayApi.slnx`.

Verification today is manual: the Bruno collection at `tests/bruno-collection.yml` (covers
health, get-by-id, authorized/declined/500 posts, and a direct simulator post), or the
`curl` lines written above each action in `Controllers/PaymentController.cs`.

## Architecture

A `POST /api/payment` flows through one chain, each link behind an interface:

`PaymentController` assigns a new `PaymentId` GUID, runs all five
`IPaymentRequestValidator` checks (`Services/PaymentRequestValidator.cs`), then hands off to
`IBankService` (`Services/BankService.cs`). `BankService` calls `IBankHttpClient`
(`Services/BankHttpClient.cs`, POSTs to the `SimulatorBankEndpoint`), passes a successful
response to `IBankResponseProcessor` (`Services/BankResponseProcessor.cs`) to map it onto a
`PostPaymentResponse`, and persists that via `IPostPaymentRepository`
(`Services/PaymentsRepository.cs`) before returning the status.

`PaymentStatus` (`Models/PaymentStatus.cs`) is the contract between layers:

- `Rejected` — local validation failed; the bank was never called.
- `Declined` — the bank answered `authorized: false`.
- `InternalError` — the bank returned non-2xx, or its body could not be parsed.

### Models come in two pairs, deliberately

`PaymentRequest` / `PostPaymentResponse` are this gateway's own API shape.
`BankRequest` / `BankResponse` model the acquiring bank's contract and use `snake_case`
through `[JsonPropertyName]` (`card_number`, `expiry_date`, `authorization_code`). Keep
them separate — collapsing them couples our public API to the bank's.

### Persistence

DynamoDB table `Payments`, hash key `PaymentId`. Each item is just `PaymentId` (S) plus
`PaymentRaw` (S) holding the JSON-serialized `PostPaymentResponse`. Only the last four card
digits are ever stored.

### Wiring

All DI registration lives in `Program.cs`. Services are scoped; the repository is a
singleton, as is the `IAmazonDynamoDB` client registered through
`AddAWSService<IAmazonDynamoDB>()`. New services follow the existing pattern — interface
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

## Conventions

- **Explicit `using` directives** — `ImplicitUsings` is disabled, so every file declares
  its own. `Nullable` is enabled.
- **`this.` prefix** on all field access, consistently throughout.
- **Structured logging** follows one template shape:
  `"MethodName, PaymentId={PaymentId}, Status={Status}"`, with `Status` values like
  `"Started"` / `"Completed"` / `"Failed"`. `CallerId` and `CorrelationId` are enriched
  automatically by `LogMiddleware` from the request headers (a missing `CorrelationId`
  header gets a generated GUID), so never pass them by hand.
- **Serilog is configured from `src/logconfig.json`, not `appsettings.json`.**
  `appsettings.json` holds `SimulatorBankEndpoint` and the `AWS` block (pointed at
  `http://localhost:8000` for DynamoDB Local). Verbose file logs go to `src/z-logs/`
  (gitignored); the console sink is deliberately terse.
