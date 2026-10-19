Building a basic payment gateway

https://en.wikipedia.org/wiki/Payment_gateway#Typical_transaction_processes
https://stripe.com/gb/resources/more/payment-gateways-101#how-does-a-payment-gateway-work


Intentionally kept simple so that it can demo software building principles like SOLID and general system design.

More verbose logs than the console ones are in directory `z-logs`.

To run / debug, use the below commands from the ```src``` folder
```bash
docker compose up
dotnet run # or debug via your IDE
```

or run everything from docker
```bash
docker compose --profile api up
## docker compose --profile api down
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

