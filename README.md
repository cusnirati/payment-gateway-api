## The objective of this project

The objective is to demo software building principles like SOLID and general system design, not to build a fully production ready solution.

Intentionally kept simple so that it can be easily followed step by step in the payment processing flow.

General notes:

- More verbose logs than the console ones are in directory `z-logs`.
- Ideally (but adding compolexity) we would have both idempotency and persisting the payment in the database once it passed validation (before calling the bank)
- For simplicity all failures return for now ```"paymentStatus": "Unavailable"```. Complex failure handling and reconciliation (a stuck payment) is out of scope.
- No authentification for now.
- Tests like full unit tests and end to end tests might not be up to date.
- I have another project to be deployed along this one (to provide an fx rate), so logic in this one might change periodically.


https://en.wikipedia.org/wiki/Payment_gateway#Typical_transaction_processes
https://stripe.com/gb/resources/more/payment-gateways-101#how-does-a-payment-gateway-work




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
  --url http://localhost:5067/api/payment \
  --header 'CallerId: bruno-client' \
  --header 'Content-Type: application/json' \
  --data '{
  "cardnumber": "2223000000000007",
  "ExpiryMonth": "11",
  "ExpiryYear": "2026",
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}'
```

