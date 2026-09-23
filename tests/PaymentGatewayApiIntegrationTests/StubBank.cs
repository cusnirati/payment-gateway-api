using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PaymentGatewayApiIntegrationTests;

// the acquiring bank, in process: answers like the WireMock simulator (by the card's last digit)
// unless a test sets up its own answer for a card, and records every request per card number.
// Tests run in parallel, so each one uses its own card numbers (Cards.Unique) and never shares state.
public sealed class StubBank : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> answers = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<JsonNode>> requests = new();

    public void Answer(string cardNumber, Func<CancellationToken, Task<HttpResponseMessage>> answer)
    {
        this.answers[cardNumber] = answer;
    }

    public IReadOnlyList<JsonNode> RequestsFor(string cardNumber)
    {
        return this.requests.TryGetValue(cardNumber, out ConcurrentQueue<JsonNode>? received) ? received.ToList() : [];
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        JsonNode body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
        string cardNumber = body["card_number"]!.GetValue<string>();
        this.requests.GetOrAdd(cardNumber, _ => new ConcurrentQueue<JsonNode>()).Enqueue(body);

        if (this.answers.TryGetValue(cardNumber, out Func<CancellationToken, Task<HttpResponseMessage>>? answer))
        {
            return await answer(cancellationToken);
        }

        int lastDigit = cardNumber[^1] - '0';
        return lastDigit switch
        {
            0 => Json(HttpStatusCode.ServiceUnavailable, "{}"),
            _ when lastDigit % 2 == 1 => Json(HttpStatusCode.OK, $$"""{"authorized":true,"authorization_code":"{{Guid.NewGuid()}}"}"""),
            _ => Json(HttpStatusCode.OK, """{"authorized":false,"authorization_code":""}""")
        };
    }
}
