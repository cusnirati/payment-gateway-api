using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PaymentGatewayApiUnitTests;

// stands in for the network under an HttpClient: records every request and answers with `respond`
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        this.respond = respond;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    // read when the request is sent, the content is disposed with the request afterwards
    public List<string> RequestBodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.Requests.Add(request);
        this.RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return this.respond(request);
    }
}
