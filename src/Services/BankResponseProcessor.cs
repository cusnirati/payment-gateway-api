using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Logging;

namespace PaymentGatewayApi.Services;

public interface IBankResponseProcessor
{
    Task<PostPaymentResponse> ParseBankResponse(PaymentRequest payment, HttpResponseMessage response);
}

public class BankResponseProcessor : IBankResponseProcessor
{
    private readonly ILogger<BankResponseProcessor> logger;

    public BankResponseProcessor(ILogger<BankResponseProcessor> logger)
    {
        this.logger = logger;
    }

    public async Task<PostPaymentResponse> ParseBankResponse(PaymentRequest request, HttpResponseMessage response)
    {
        // todo try catch
        BankResponse? bankResponse = await response.Content.ReadFromJsonAsync<BankResponse>();
        PostPaymentResponse payResponse = new();

        if (bankResponse is null)
        {
            this.logger.LogError("ParseBankResponse.NullBankResponse, Status={Status}", "Failed");

            payResponse.Status = PaymentStatus.InternalError;
            return payResponse;
        }

        payResponse.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        payResponse.CardNumberLastFour = request.CardNumber.Substring(request.CardNumber.Length - 4);

        payResponse.PaymentId = request.PaymentId;
        payResponse.ExpiryMonth = request.ExpiryMonth;
        payResponse.ExpiryYear = request.ExpiryYear;
        payResponse.Currency = request.Currency;
        payResponse.Amount = request.Amount;

        // todo double check log format. no paymentId
        this.logger.LogInformation(
            "ParseBankResponse.Completed, Authorized={Authorized}, AuthorizationCode={AuthorizationCode}",
            bankResponse.Authorized, bankResponse.AuthorizationCode);

        return payResponse;
    }
}