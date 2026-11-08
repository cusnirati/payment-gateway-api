## The objective of this project

The objective is to demo software building principles like SOLID and general system design, not to build a fully production ready solution.

Building a simple payment gateway

https://en.wikipedia.org/wiki/Payment_gateway#Typical_transaction_processes

https://stripe.com/gb/resources/more/payment-gateways-101#how-does-a-payment-gateway-work

The implementation is intentionally kept simple (see the out-of-scope list below) so the payment processing flow is easy to follow.

## How it works

A merchant posts a card payment. The gateway validates it, forwards it to the acquiring bank (simulated with WireMock), stores the result in DynamoDB and returns the payment status to the merchant.

```
PaymentController
  ├─ IPaymentsRepository.Get     check if the payment was not already processed
  ├─ IPaymentRule (x6)           validation, every rule runs and all errors are returned together
  └─ PaymentProcessor
       ├─ BankHttpClient         POST /payments to the bank (retry + timeout)
       ├─ BankResponseProcessor  bank response → Authorized / Declined / Unavailable
       └─ PaymentsRepository.Put store the result in DynamoDB
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker with Docker Compose

## To run / debug
use the below commands from the ```src``` folder
```bash
docker compose up
dotnet run # or debug via your IDE
```

or run everything from docker

```bash
docker compose --profile api up
```

`docker compose up` starts DynamoDB Local on port 8000 (and creates the `Payments` table) and the bank simulator on port 8080. The API runs on http://localhost:5067, with Swagger at http://localhost:5067/swagger.

When running the API outside docker, the AWS SDK needs some credentials to sign its DynamoDB calls. DynamoDB Local accepts any value, so if you don't already have an AWS profile set up, export dummy ones before `dotnet run`:
```bash
export AWS_ACCESS_KEY_ID=dummy AWS_SECRET_ACCESS_KEY=dummy
```


## Sending a payment

To send a payment, use postman / bruno and the collection in ```tests/bruno-collection.yml```

or send directly via curl:
```bash
curl --request POST \
  --url http://localhost:5067/api/payments \
  --header 'CallerId: bruno-client' \
  --header 'Content-Type: application/json' \
  --data '{
  "cardNumber": "2223000000000007",
  "expiryMonth": 11,
  "expiryYear": 2028,
  "currency": "GBP",
  "amount": 100,
  "cvv": "123",
  "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d"
}'
```

## Out of scope:
- Persisting the payment in the database once it passed validation (before calling the bank).
- All failures return ```"paymentStatus": "Unavailable"```. No complex failure handling and reconciliation (process a stuck payment).
- No authentication (hence no filtering by merchantId when getting the payment info).
- No healthcheck against the database
- No certificates / https
- Idempotency only looks at the `paymentId`. Resending a stored `paymentId` with a different amount, card or currency returns the original payment instead of being rejected.
- Concurrent requests with the same `paymentId` are not locked: both can reach the bank (the bank is assumed to be idempotent on `paymentId`). The database never overwrites an `Authorized`/`Declined` payment, but the losing request still returns its own bank result.
- If storing the payment fails after the bank has answered, the merchant still gets the bank's status, but a later GET returns 404. This would be covered by monitoring and alerting.
- The stored payment format isn't versioned.
- Backoff and a circuit breaker on the bank call.




## API

### `POST /api/payments`

Request:

| Field | Type | Rule |
|---|---|---|
| `paymentId` | GUID | Supplied by the merchant, unique per payment. Used as the idempotency key. |
| `cardNumber` | string | 14 to 19 digits, must pass the Luhn checksum. |
| `expiryMonth` | number | 1 to 12. |
| `expiryYear` | number | Four-digit year. The expiry month/year must not be before the current month. |
| `cvv` | string | 3 or 4 digits. |
| `amount` | number | Whole number of minor currency units (100 = 1.00 GBP), at least 1. |
| `currency` | string | One of `SupportedCurrencies`. Case-insensitive, `gbp` is accepted and stored as `GBP`. |

Responses:
- `201 Created` with the payment (see below) and a `Location` header pointing to the GET endpoint. Returned for every payment the bank was asked about, including `Declined` and `Unavailable`.
- `400 Bad Request` when validation fails. The bank isn't called and nothing is stored.
- `500 Internal Server Error` on an unexpected failure.

Idempotency: resending a `paymentId` that is stored as `Authorized` or `Declined` returns the stored result without calling the bank again. Resending one stored as `Unavailable` retries it against the bank.

### `GET /api/payments/{paymentId}`

- `200 OK` with the payment.
- `404 Not Found` when no payment has that id.
- `500 Internal Server Error` when the stored payment is corrupt.

### Payment response

```json
{
  "paymentId": "1bfb4527-35b5-4a0c-84d1-c3227fd63b5d",
  "paymentStatus": "Authorized",
  "amount": 100,
  "currencyCode": "GBP"
}
```

`paymentStatus` is one of:
- `Authorized`: the bank authorized the payment.
- `Declined`: the bank declined the payment.
- `Unavailable`: the payment is valid, but the bank couldn't be reached, failed (even after the retry), or answered outside its contract. The payment can be retried by resending the same `paymentId`.
- `Unknown`: never returned for a valid stored payment; it's the default value of the enum.


## Bank simulator

The simulator's answer depends on the card number's last digit:

| Last digit | Bank answer | `paymentStatus` | Example card |
|---|---|---|---|
| odd (1, 3, 5, 7, 9) | authorized, with an authorization code | `Authorized` | `2223000000000007` |
| even (2, 4, 6, 8) | declined | `Declined` | `5123450000000008` |
| 0 | `503 Service Unavailable` | `Unavailable` | `4318722273695740` |

A request missing fields gets a `400` from the simulator, which the gateway also reports as `Unavailable`. The mappings are in `src/Simulators/wiremock/mappings/`.



## Logs

The console shows a short version of each log line. More verbose logs (with `CallerId`, `TraceId` and `SpanId`) are written to the `z-logs` directory

## Tests

From the repo root:
```bash
dotnet test --project tests/PaymentGatewayApiUnitTests
dotnet test --project tests/PaymentGatewayApiEndToEndTests   # needs `docker compose up` running from src
```

Always pass `--project`; a bare `dotnet test` fails.
