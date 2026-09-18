# Logging Audit — payment-gateway

**Date:** 2026-09-18
**Branch:** `claude-123` @ `81a37c0`
**Scope:** every `this.logger.*` call site in the project.

---

## What was audited

17 `this.logger.*` call sites across 7 files, out of 14 C# files total. Logging goes
through `Microsoft.Extensions.Logging.ILogger<T>`, backed by Serilog
(`Serilog.AspNetCore` 10.0.0), configured in `src/logconfig.json` and wired at
`src/Program.cs:11-13` via `builder.Host.UseSerilog(log)`.

### Inventory

| File | Calls | Levels used |
|---|---|---|
| `src/Services/PaymentRequestValidator.cs` | 6 | `LogInformation` ×6 |
| `src/Controllers/PaymentController.cs` | 3 | `LogInformation` ×3 |
| `src/Services/BankService.cs` | 2 | `LogInformation`, `LogError` |
| `src/Services/BankResponseProcessor.cs` | 2 | `LogInformation`, `LogError` |
| `src/Services/PaymentsRepository.cs` | 2 | `LogError`, `LogWarning` |
| `src/Services/BankHttpClient.cs` | 1 | `LogInformation` |
| `src/Middleware/ErrorMiddleware.cs` | 1 | `LogCritical` |

Level distribution: `LogInformation` ×12, `LogError` ×3, `LogWarning` ×1,
`LogCritical` ×1. `LogDebug`, `LogTrace` and `BeginScope` are never used.

`src/Middleware/LogMiddleware.cs` has no logger of its own — it only pushes
`CallerId` and `CorrelationId` onto Serilog's `LogContext`, generating a GUID when the
`CorrelationId` header is absent.

### What is already consistent

- **Instantiation.** All 8 classes use an identical pattern: generic `ILogger<T>`
  injected via constructor into a `private readonly ILogger<T> logger;` field. No
  static loggers, no hardcoded category strings, no `new Logger(...)`. `SourceContext`
  therefore comes free and is rendered by both file sinks.
- **Structured logging.** Every one of the 17 calls uses a Serilog message template
  with named PascalCase placeholders and positional arguments. There is **zero** string
  interpolation or concatenation inside a log message anywhere in the project.
- **Correlation.** `PaymentId={PaymentId}` appears on 16 of 17 calls (all but
  `GetHealthcheck.Received`, which has no payment in scope).
- **No emoji, no `[ClassName]` prefixes, no trailing punctuation.**

---

## A. Bugs

### A1. Two validators log the wrong operation name

`src/Services/PaymentRequestValidator.cs:83` and `:98` both emit
`"Validator.IsAmountValid, ..."`, but they live in `IsCurrencyCodeValid` and
`ValidateCvv` respectively.

Three unrelated failure modes — invalid amount, invalid currency, invalid CVV — all
report under the single operation name `Validator.IsAmountValid`. Log-based alerting on
currency or CVV rejection is impossible without parsing the free-text `Reason` field,
and an amount-failure count is inflated by two other causes.

### A2. `CorrelationId` and `CallerId` are always empty on unhandled exceptions

`src/Program.cs:34-35` registers `ErrorMiddleware` **before** `LogMiddleware`:

```csharp
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<LogMiddleware>();
```

That makes `ErrorMiddleware` the outer middleware. The `LogContext.PushProperty`
`using` blocks in `LogMiddleware.cs:32-36` unwind as an exception propagates outward, so
by the time `ErrorMiddleware.cs:27` runs its `LogCritical`, both properties are out of
scope.

Both file sink templates in `logconfig.json` render
`CallerId={CallerId:l}, CorrelationId={CorrelationId:l}` — so these fields are blank on
every 500. Correlation is missing from precisely the logs where it matters most.

### A3. Silent throw in the repository

`src/Services/PaymentsRepository.cs:78` throws on a non-OK DynamoDB status with no log
at all:

```csharp
if (response.HttpStatusCode != HttpStatusCode.OK)
{
    throw new Exception();
}
```

The mirror-image path in `Add` (`:49-53`) logs `LogError` before throwing. A failed read
surfaces only as a bare parameterless `Exception` in `ErrorMiddleware`, with no
`PaymentId` and — per A2 — no `CorrelationId`.

### A4. Stray `}` in the console output template

`src/logconfig.json`, console sink:

```
"[{Timestamp:HH:mm:ss.fff}] [{Level:u3}]}, {Message}{NewLine}"
```

The `}` after `[{Level:u3}]` is a literal typo, emitted on every console line.

---

## B. Naming inconsistencies

The dominant convention is `PascalOperation.Outcome` with outcome ∈
{`Received`, `Completed`, `Failed`} and `, ` separators. Deviations:

| Location | Current | Convention suggests |
|---|---|---|
| `PaymentsRepository.cs:50` | `Add.Payments.Failed` | `AddPayment.Failed` |
| `PaymentsRepository.cs:91` | `Get.Payments.Failed` | `GetPayment.NotFound` |
| `PaymentRequestValidator.cs` (×6) | `Validator.IsCardNumberValid` — class prefix, no outcome | `ValidateCardNumber.Rejected` |
| `BankResponseProcessor.cs:32` | `ParseBankResponse.NullBankResponse` — outcome slot holds a cause | `ParseBankResponse.Failed, Reason=...` |
| `BankService.cs:33,45` | `ExecuteBankPayment.*` | method is actually `ExecutePayment` |
| `ErrorMiddleware.cs:29` | `"Operation {Operation} {Status}. StatusCode={StatusCode} Message={Message}"` | `UnhandledException.Failed, StatusCode={StatusCode}` |

Four distinct shapes are in use: class-scoped (`Validator.*`), method-scoped
(`GetPayment.*`, `PostBankPayment.*`), method+entity (`Add.Payments.*`), and
method-renamed (`ExecuteBankPayment.*`).

`ErrorMiddleware` is the largest outlier. It templates the operation *name* as a
parameter rather than using a literal prefix, and separates fields with `. ` and ` `
where every other call site uses `, `. Serilog groups events by message template, so
this line cannot be grouped alongside anything else. It also passes
`Message={Message}` carrying `exception.Message`, which duplicates what Serilog already
renders from the exception object passed as the first argument (the error sink template
appends `Exception={Exception}`), and hardcodes the literal `500` while the response
below it uses `StatusCodes.Status500InternalServerError`.

Note also that `Get.Payments.Failed` (repository) reads as a sibling of
`GetPayment.Received` (controller) but is a different operation.

---

## C. Three names for one concept — and all three are constants

- `Status={Status}` — hardcoded `"Failed"` at `BankService.cs:45` and
  `BankResponseProcessor.cs:32`; hardcoded `"NotFound"` at `PaymentsRepository.cs:91`
- `OperationStatus={OperationStatus}` — hardcoded `"Failed"` at `PaymentsRepository.cs:51`
- `IsValid={IsValid}` — hardcoded `false` at all six validator call sites

Ten of the 17 calls carry at least one placeholder whose value is a literal at the call
site and can never vary. None of them add information: the outcome is already encoded in
the log level and the `.Failed` message suffix.

`Status` is the most problematic of the three, because it collides with the domain's
real `PaymentStatus` enum (`Authorized` / `Declined` / `Rejected` / `InternalError`, see
`src/Models/PaymentStatus.cs`). A log field named `Status` that always reads `"Failed"`
will mislead anyone querying these logs.

`PaymentsRepository.cs:50` additionally logs `ContentLength={ContentLength}`, which has
no diagnostic value on a failed write.

The one field that does vary is `Reason={Reason}` in the validator — sentence-case, no
terminating period: `"Month out of range"`, `"Card expired"`, `"Invalid amount"`,
`"Invalid currency"`, `"Invalid CVV"`.

---

## D. Log level has no consistent rule

Equivalent outcomes are spread across four severities:

| Event | Level | Location |
|---|---|---|
| Validation rejection (client's fault) | `LogInformation` ×6 | `PaymentRequestValidator.cs` |
| Payment not found (client's fault) | `LogWarning` | `PaymentsRepository.cs:91` |
| Bank returned non-2xx | `LogError` | `BankService.cs:45` |
| Null bank response | `LogError` | `BankResponseProcessor.cs:32` |
| DynamoDB write failed | `LogError` | `PaymentsRepository.cs:49` |
| Any unhandled exception | `LogCritical` | `ErrorMiddleware.cs:27` |

Two problems:

1. **"Not found" and "validation rejected" are the same class of event** — a routine,
   client-driven outcome — but sit a level apart.
2. **`LogCritical` on every unhandled exception is too strong.** `Critical`
   conventionally means process-level or unrecoverable; here it fires on any single bad
   request that trips a `NullReferenceException`. Because the error sink is
   `restrictedToMinimumLevel: Warning`, every one of these lands in `error-.log` and
   would trigger any level-based alert.

A rule that would fit this codebase:

- **Information** — client-caused rejections and normal lifecycle (validation, not found)
- **Warning** — degraded but handled (bank declined, retry exhausted)
- **Error** — our fault or a dependency failure (Dynamo write failed, null bank response, unhandled exception)
- **Critical** — reserved for startup/shutdown/process-level failure; not used per-request

---

## E. Asymmetric coverage

- `BankService.ExecutePayment` (`:33`) logs `Received` and `Failed`, but the success
  path (`:36-42`) returns silently — no `Completed`.
- `BankHttpClient.PostBankPayment` (`:56`) logs `Completed` with no matching start log —
  the exact inverse. It also logs at `Information` **regardless of status code**, so a
  bank 500 and a bank 200 are indistinguishable by level.
- `PaymentController` logs `Received` on all three endpoints and never logs an outcome.
  The `NotFoundObjectResult` (`:53`), the `BadRequestObjectResult` on validation failure
  (`:74`) and the `BadRequestObjectResult` on bank failure (`:85`) are all silent at the
  controller layer.
- `PaymentsRepository.Add` and `Get` log only failures, never success.
- `GetHealthcheck` (`:39`) logs at `Information` on every probe — this will dominate log
  volume once a load balancer polls it.
- `Program.cs` emits no startup or shutdown line, so a log file cannot be tied to a
  process lifetime.

There is no diagnostic tier below `Information` anywhere in the project, which is part
of why the healthcheck and per-validation lines sit where they do.

**Consequence:** you cannot compute request latency, and success and failure counts do
not reconcile, because most operations only log one side of the pair.

---

## F. Sensitive data — clean, with one item to confirm

**No card number, CVV, or expiry date reaches any log.** This holds even where it would
be easy to slip: `BankHttpClient.cs:35-42` builds a `BankRequest` containing the full
PAN and CVV and has both in scope at the log statement on `:56`, and correctly
interpolates neither. `CardNumberLastFour` is properly truncated at
`BankResponseProcessor.cs:39` and is not logged in any case. No email address, no API
key, and no full request body is logged anywhere.

**One item to confirm:** `AuthorizationCode={AuthorizationCode}` is logged in full, in
cleartext, at `BankResponseProcessor.cs:47-49` — to both the console and
`z-logs/info-*.log`, on every successful payment. Per
`src/Models/Responses/BankResponse.cs` this is a bank-issued token
(`"authorization_code": "0bb07405-6d44-4b50-a14f-7ae0beff13ad"`). This is often
acceptable, but it should be checked against the target compliance regime; if it is not
permitted, log a hash or a truncated form.

**Adjacent, not a logging issue:** `ErrorMiddleware.cs:35` writes `exception.Message`
into the HTTP response body. The in-file comment
`// put correlationId into returned message and not exception.Message` already flags it.

---

## G. Config and hygiene

1. **Two competing logging configs.** `src/appsettings.json` and
   `src/appsettings.Development.json` both carry a `Logging:LogLevel` section, but
   `Program.cs:13` calls `UseSerilog(log)` with a pre-built logger, which replaces the
   logging providers. Those sections are dead config.
2. **The console sink drops all context.** Both file templates include `CallerId`,
   `CorrelationId` and `SourceContext`; the console template includes none of them, so
   console output cannot be correlated to a request.
3. **`error-.log` contains warnings.** Its threshold is `restrictedToMinimumLevel:
   Warning`, so the filename is misleading; everything in it is also duplicated into
   `info-.log`.
4. **Unused `using Serilog;`** at `BankHttpClient.cs:8` and `BankService.cs:6`. Both
   use `ILogger<T>` from `Microsoft.Extensions.Logging` and reference no Serilog type.
   The other five logging files do not have this import.
5. **Filename/class mismatch.** `PaymentsRepository.cs` declares class
   `PostPaymentRepository`, so `SourceContext` in the logs will not match the filename.
6. **Stale TODO.** `PaymentRequestValidator.cs:17` reads `// todo add logs for invalid`,
   but those logs exist.
7. **AWS SDK exception used for an application error.** `PaymentsRepository.cs:53`
   throws `InternalServerErrorException`, which resolves via
   `using Amazon.DynamoDBv2.Model;` to `Amazon.DynamoDBv2.Model.InternalServerErrorException`
   (package `AWSSDK.DynamoDBv2`). It is not a project-defined type. Callers catching it
   would be catching an AWS SDK type to handle a gateway failure.
8. **No test coverage of logging.** `tests/` contains only `bruno-collection.yml`, with
   no assertions on log output.
9. **Four open `todo` comments** mark known unlogged failure paths:
   `BankHttpClient.cs:49` (`// todo exception catch`),
   `BankResponseProcessor.cs:26` (`// todo try catch`),
   `PaymentsRepository.cs:45` (`// todo add try catch`) and `:57` (`// todo same as above`).

---

## Summary

The logging foundation is sound — consistent DI, structured templates everywhere, no
string interpolation, good `PaymentId` coverage, and no PAN/CVV leakage. The issues are
in the details: one copy-paste defect that attributes failures to the wrong validator,
a middleware ordering bug that strips correlation IDs from every 500, one unlogged
throw, a typo in the console template, and — across sections B through E — a set of
conventions that are followed *nearly* everywhere but not quite consistently enough to
query reliably.
