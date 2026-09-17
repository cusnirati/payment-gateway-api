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
        PostPaymentResponse paymentResponse = new();

        if (bankResponse is null)
        {
            this.logger.LogError("ParseBankResponse.NullBankResponse, Status={Status}", "Failed");

            paymentResponse.Status = PaymentStatus.InternalError;
            return paymentResponse;
        }

        paymentResponse.Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined;
        paymentResponse.CardNumberLastFour = request.CardNumber.Substring(request.CardNumber.Length - 4);

        paymentResponse.PaymentId = request.PaymentId;
        paymentResponse.ExpiryMonth = request.ExpiryMonth;
        paymentResponse.ExpiryYear = request.ExpiryYear;
        paymentResponse.Currency = request.Currency;
        paymentResponse.Amount = request.Amount;

        // todo double check log format. no paymentId
        this.logger.LogInformation(
            "ParseBankResponse.Completed, Authorized={Authorized}, AuthorizationCode={AuthorizationCode}",
            bankResponse.Authorized, bankResponse.AuthorizationCode);

        return paymentResponse;
    }
}