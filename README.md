## The objective of this project

The objective is to demo software building principles like SOLID and general system design, not to build a fully production ready solution.

Building a simple payment gateway

https://en.wikipedia.org/wiki/Payment_gateway#Typical_transaction_processes

https://stripe.com/gb/resources/more/payment-gateways-101#how-does-a-payment-gateway-work

More verbose logs than the console ones are in directory `z-logs`.

Intentionally kept simple (see below) so that it can be easily followed the payment processing flow

## Out of scope:
- Idempotency and persisting the payment in the database once it passed validation (before calling the bank).
- All failures return ```"paymentStatus": "Unavailable"```. No complex failure handling and reconciliation (process a stuck payment).
- No authentication (hence no filtering by merchantId when getting the payment info).
- No healthcheck against the database



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


To send a payment, use postman / bruno and the collection in ```tests/bruno-collection.yml```

or send directly via curl
```bash
curl --request POST \
  --url http://localhost:5067/api/payments \
  --header 'CallerId: bruno-client' \
  --header 'Content-Type: application/json' \
  --data '{
  "cardnumber": "2223000000000007",
  "ExpiryMonth": "11",
  "ExpiryYear": "2028",
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}'
```

