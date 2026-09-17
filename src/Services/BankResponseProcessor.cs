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
        PostPaymentResponse result = new();

        if (bankResponse is null)
        {
            this.logger.LogError("ParseBankResponse.NullBankResponse, Status={Status}", "Failed");

            result.Status = PaymentStatus.InternalError;
            return result;
        }

        result.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        result.CardNumberLastFour = request.CardNumber.Substring(request.CardNumber.Length - 4);

        result.PaymentId = request.PaymentId;
        result.ExpiryMonth = request.ExpiryMonth;
        result.ExpiryYear = request.ExpiryYear;
        result.Currency = request.Currency;
        result.Amount = request.Amount;

        this.logger.LogInformation(
            "ParseBankResponse.Completed, Authorized={Authorized}, AuthorizationCode={AuthorizationCode}",
            bankResponse.Authorized, bankResponse.AuthorizationCode);

        return result;
    }
}