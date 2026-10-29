using System;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PaymentGatewayApi.Models;
using PaymentGatewayApi.Models.Requests;
using PaymentGatewayApi.Models.Responses;

namespace PaymentGatewayApi.Services;

public interface IBankResponseProcessor
{
    PaymentDb ParseBankResponse(PaymentRequest payment, BankResponse? response);
}

public class BankResponseProcessor : IBankResponseProcessor
{
    private readonly ILogger<BankResponseProcessor> logger;

    public BankResponseProcessor(ILogger<BankResponseProcessor> logger)
    {
        this.logger = logger;
    }

    public PaymentDb ParseBankResponse(PaymentRequest paymentRequest, BankResponse? bankResponse)
    {
        PaymentDb paymentDb = new PaymentDb();
        paymentDb.PaymentId = paymentRequest.PaymentId;

        paymentDb.CardNumberLastFour = paymentRequest.CardNumber.Substring(paymentRequest.CardNumber.Length - 4);
        paymentDb.ExpiryMonth = paymentRequest.ExpiryMonth;
        paymentDb.ExpiryYear = paymentRequest.ExpiryYear;

        paymentDb.Amount = paymentRequest.Amount;
        paymentDb.Currency = paymentRequest.Currency;

        // the bank could not be reached or failed even after the retry
        if (bankResponse is null)
        {
            this.logger.LogError("PaymentId={PaymentId}, Operation={Operation}, Reason={Reason}, Status={Status}", paymentRequest.PaymentId, "ParseBankResponse", "NullResponse", "Failed");

            paymentDb.Status = PaymentStatus.Unavailable;
            return paymentDb;
        }

        // authorized without an authorization code breaks the bank's contract: the outcome is unknown, not a decline
        if (bankResponse.Authorized && string.IsNullOrWhiteSpace(bankResponse.AuthorizationCode))
        {
            this.logger.LogError("PaymentId={PaymentId}, Operation={Operation}, Reason={Reason}, Status={Status}", paymentRequest.PaymentId, "ParseBankResponse", "AuthorizedWithoutAuthorizationCode", "Failed");

            paymentDb.Status = PaymentStatus.Unavailable;
            return paymentDb;
        }

        paymentDb.BankAuthorizationCode = bankResponse.AuthorizationCode;
        if (bankResponse.Authorized)
        {
            paymentDb.Status = PaymentStatus.Authorized;
        }
        else
        {
            paymentDb.Status = PaymentStatus.Declined;
            paymentDb.BankReason = bankResponse.Reason;
        }

        this.logger.LogInformation(
            "PaymentId={PaymentId}, Operation={Operation}, Status={Status}, Authorized={Authorized}",
            paymentDb.PaymentId, "ParseBankResponse", "Completed", paymentDb.Status);

        return paymentDb;
    }
}