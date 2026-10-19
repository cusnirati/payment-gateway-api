# AGENTS.md

Demo payment gateway: ASP.NET Core (.NET 10) API that validates a card payment, forwards it to a
simulated acquiring bank, and stores the result in DynamoDB.

## Ground rules

- **Never execute any git commands** unless the user explicitly asks for it in that request.
- **Never write or modify tests** unless the user explicitly asks for it.
- **Never add private or static methods** unless for tests.
- **Never add cancellation tokens** unless the user explicitly asks for it.
- **Never add partial or sealed classes** unless the user explicitly asks for it.

## Run

Dependencies (DynamoDB Local on :8000, WireMock bank simulator on :8080), from `src/`:

    docker compose up -d                  # add --profile api to also run the API container on :5067

    dotnet build PaymentGatewayApi.slnx
    dotnet run --project src/PaymentGatewayApi.csproj      # http://localhost:5067/swagger

## Test

    dotnet test --project tests/PaymentGatewayApiUnitTests

Always pass `--project`; a bare `dotnet test` fails. Manual checks: `tests/bruno-collection.yml`.
The simulator's response depends on the card number's last digit, see `src/Simulators/wiremock/mappings/`.

## Layout

- `src/Controllers` → `src/Services` (validators, `PaymentProcessor`, bank client, repository) → `src/Models`
- DI wiring, problem-details setup and middleware order all live in `src/Program.cs`.
- `BankRequest`/`BankResponse` mirror the bank's contract; keep them separate from our own API models.

## Conventions

- `ImplicitUsings` is off: declare every `using`. `Nullable` is on.
- Prefix field access with `this.`.
- Interface and implementation live in the same file if there is only 1 implementation.
- Log with the template `"PaymentId={PaymentId}, Operation={Operation}, Status={Status}"`.
- Error responses are RFC 9457 problem details; don't hand-roll error bodies.
