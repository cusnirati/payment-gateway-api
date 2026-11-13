# Code patterns (PAT-*)

How code is written here. Mechanical and checkable in a diff. Same format and ID rules as
`architecture.md`: the rule, **Why**, **Check**; IDs are never renumbered or reused.

---

## C# style

### PAT-001 Explicit `using`s, `System*` first then alphabetical
`ImplicitUsings` is off in every project. Declare each `using`; `System` and `System.*` first, the rest alphabetical.
- **Check:** build passes; new usings follow the order.

### PAT-002 Nullable reference types are on, and mean something
`Nullable` is enabled. "Not found" or "no answer" is a nullable return (`Task<PaymentDb?>`, `BankResponse?`, `ValidationError?`), not an exception. Mandatory request fields are `required`.
- **Check:** no `!` null-forgiving operator in `src/` without a comment saying why.

### PAT-003 `this.` on every instance member access
Fields, properties and inherited members: `this.logger`, `this.ModelState`, `this.Problem(...)`.
- **Check:** member access without `this.` in a diff.

### PAT-004 File-scoped namespaces matching the folder
`namespace PaymentGatewayApi.Services.Validators;` for `src/Services/Validators/`.

### PAT-005 Interface next to its only implementation
One implementation → interface and class in the same file (`IBankHttpClient` in `BankHttpClient.cs`). Several implementations → interface in its own file (`IPaymentRule.cs`).

### PAT-006 Classic constructor injection into `private readonly` fields
Explicit constructor, one assignment per dependency, field named after the dependency (`logger`, `timeProvider`). No primary constructors, no service locator, no `new` of a service. Options are unwrapped in the constructor (`this.options = options.Value`).

### PAT-007 No private or static methods, no partial or sealed classes
Logic stays in the public interface method. Exceptions: tests; `static readonly` fields such as compiled `Regex` are fine. (Also AGENTS.md ground rules.)
- **Why:** keeps every behaviour reachable and testable through the interface.
- **Check:** `private ... (`, `static ... (`, `partial`, `sealed` in a `src/` diff.

### PAT-008 Async methods return `Task`, no `Async` suffix, always awaited
`Get`, `Put`, `PostPayment`, `ExecutePayment`. No `.Result`, `.Wait()` or `async void`.

### PAT-009 Fixed names are `private const string`
e.g. `PaymentsTable`. No magic strings repeated in a class.

### PAT-019 Explicit types on local declarations, target-typed `new()`
Declare locals with their type, never `var`: `PaymentDb? payment = await this.paymentRepo.Get(...)`,
`WebApplicationBuilder builder = ...`. When constructing, don't repeat the type: `PaymentDb paymentDb = new();`,
`Dictionary<string, AttributeValue> item = new() { ... }`. Applies to `src/`.
- **Why:** the type (and its nullability) is visible at the declaration without an IDE.
- **Check:** `var ` or `T x = new T(` in a `src/` diff.

### PAT-010 Comments say why, not what
Short, lower-case, at the line they explain. Assumptions and scope cuts are written where they apply (`// we assume the bank call is idempotent`, `// out of scope`). Example JSON payloads sit above request/response model classes.

---

## Models

### PAT-011 Models are plain classes with `{ get; set; }`
No behaviour in models. JSON names default to camelCase; `[JsonPropertyName]` only where an external contract differs (bank snake_case).

### PAT-012 Enums: explicit values, `Unknown = 0`, serialized as strings
`[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, every member numbered, a comment on each saying when it applies.

---

## Logging

### PAT-013 One structured template
`"PaymentId={PaymentId}, Operation={Operation}, Status={Status}"`, optionally extended with `, Reason={Reason}` or another `Key={Key}` pair. Without a payment in scope, drop `PaymentId`. The exception is the first argument when there is one. No string interpolation in the template.
- **Operation:** PascalCase verb-noun (`PostBankPayment`, `PutPayment`); validators use `Validator.Is<Field>Valid`.
- **Status:** `Started`, `Completed`, `Replayed`, `Invalid`, `Failed`, `AlreadyExists`. Reuse these before inventing new ones.
- **Level:** `Information` for flow steps and validation failures, `Error` for failures, `Fatal` only for startup.
- **Check:** template shape; no card number or CVV (ARCH-003).

---

## Controllers and errors

### PAT-014 Controller action shape
`[ApiController]`, `[Route("api/...")]`, `ActionResult<T>` return, a `[ProducesResponseType]` for every status (errors with `"application/problem+json"`). Route constraints for typed IDs (`{paymentId:guid}`). `201` via `CreatedAtAction`. Errors via `this.Problem(statusCode:, detail:)` or `this.ValidationProblem()` (ARCH-013).

### PAT-015 Validation rule shape
Class `<Thing>Rule : IPaymentRule` in `src/Services/Validators`. Returns `null` when valid, else a `ValidationError` with `Field = nameof(PaymentRequest.X)`. Logs at `Information` with `Status=Invalid` before returning. Message states the requirement and ends with a period; it quotes the offending value only when it isn't card data: `"Expiry month '13' is not valid. Expiry month must be 1 to 12."`. `Field` is always a real `PaymentRequest` property so clients can map it to an input; an error spanning several fields goes under the last one (an expired month/year is reported under `ExpiryYear`). Regexes are `private static readonly ... RegexOptions.Compiled` and anchored with `^...\z`.

---

## Tests (only when a task asks for them)

### PAT-016 Unit tests
xUnit v3 + Moq. One test class per production class, same folder structure under `tests/PaymentGatewayApiUnitTests`. Collaborators are `MockBehavior.Strict`, the logger is loose and checked with `LoggerMockExtensions`. Shared inputs from `TestPayments.Create()`. Names: `Method_Condition_Expectation`. Time via a fake `TimeProvider`.

### PAT-017 End-to-end tests
The real API in-process (`WebApplicationFactory<Program>`) against the docker compose DynamoDB Local and WireMock. Not parallelized. Card numbers pick the simulator behaviour (last digit).

### PAT-018 Running tests
`dotnet test --project <path>`; a bare `dotnet test` fails.
