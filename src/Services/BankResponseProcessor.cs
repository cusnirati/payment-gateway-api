using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using PaymentGatewayApi.Models;
using Microsoft.Extensions.Logging;
using System.Net;

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
        BankResponse? bankResponse;
        PostPaymentResponse payResponse = new();

        try
        {
            bankResponse = await response.Content.ReadFromJsonAsync<BankResponse>();
        }
        catch (Exception exception)
        {
            this.logger.LogError(exception, "ParseBankResponse exception, PaymentId={PaymentId}, Status={Status}", request.PaymentId, "Failed");

            payResponse.Status = PaymentStatus.InternalError;
            return payResponse;
        }

        if (bankResponse is null)
        {
            this.logger.LogError("ParseBankResponse null response, PaymentId={PaymentId}, Status={Status}", request.PaymentId, "Failed");

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

        this.logger.LogInformation(
            "ParseBankResponse, PaymentId={PaymentId}, Status={Status}, Authorized={Authorized}",
            request.PaymentId, "Completed", bankResponse.Authorized);

        return payResponse;
    }
}