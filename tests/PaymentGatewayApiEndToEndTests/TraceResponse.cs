using System.Net.Http;
using Xunit;

namespace PaymentGatewayApiEndToEndTests;

public static class TraceResponse
{
    // the traceresponse header carries the request's W3C trace context: 00-{trace id}-{span id}-{flags}
    public static string TraceIdOf(HttpResponseMessage response)
    {
        string traceResponse = Assert.Single(response.Headers.GetValues("traceresponse"));
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceResponse);
        return traceResponse.Split('-')[1];
    }
}
