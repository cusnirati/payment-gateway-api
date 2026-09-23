using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Models;

namespace PaymentGatewayApi.Services;

public interface IPaymentRequestValidator
{
    // every rule the request breaks, keyed by its JSON field name; empty when the request is valid
    IReadOnlyDictionary<string, string[]> Validate(PaymentRequest request);
}

public partial class PaymentRequestValidator : IPaymentRequestValidator
{
    // cards are issued with an expiry a few years out, anything further is a typo or garbage
    private const int MaxExpiryYearsAhead = 20;

    private readonly ILogger<PaymentRequestValidator> logger;
    private readonly TimeProvider timeProvider;
    private readonly HashSet<string> supportedCurrencies;

    public PaymentRequestValidator(
        ILogger<PaymentRequestValidator> logger, TimeProvider timeProvider, IOptions<PaymentOptions> options)
    {
        this.logger = logger;
        this.timeProvider = timeProvider;
        this.supportedCurrencies = new HashSet<string>(options.Value.SupportedCurrencies, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> Validate(PaymentRequest request)
    {
        Dictionary<string, string[]> errors = new();

        // no Luhn check: the bank simulator picks its answer from the last digit, which a Luhn
        // check digit would fix, and the issuer validates the number anyway
        if (!CardNumberPattern().IsMatch(request.CardNumber))
        {
            errors["cardNumber"] = ["Must be 14 to 19 digits."];
        }

        if (request.ExpiryMonth is < 1 or > 12)
        {
            errors["expiryMonth"] = ["Must be between 1 and 12."];
        }
        else if (this.IsExpired(request))
        {
            errors["expiryYear"] = ["The card has expired."];
        }
        else if (request.ExpiryYear > this.timeProvider.GetUtcNow().Year + MaxExpiryYearsAhead)
        {
            errors["expiryYear"] = [$"Must be at most {MaxExpiryYearsAhead} years ahead."];
        }

        if (!this.supportedCurrencies.Contains(request.Currency))
        {
            errors["currency"] = [$"Must be one of {string.Join(", ", this.supportedCurrencies)}."];
        }

        if (request.Amount <= 0)
        {
            errors["amount"] = ["Must be a positive amount in the currency's minor unit."];
        }

        if (!CvvPattern().IsMatch(request.Cvv))
        {
            errors["cvv"] = ["Must be 3 or 4 digits."];
        }

        if (errors.Count > 0)
        {
            this.LogRejected("Rejected", errors.Keys);
        }

        return errors;
    }

    // compares year and month directly, building a DateTime would throw for an out of range year;
    // UTC, not the server's time zone, and a card is valid through the last day of its expiry month
    private bool IsExpired(PaymentRequest request)
    {
        DateTimeOffset now = this.timeProvider.GetUtcNow();
        return request.ExpiryYear < now.Year || (request.ExpiryYear == now.Year && request.ExpiryMonth < now.Month);
    }

    // [0-9], not \d, which also matches other scripts' digits
    [GeneratedRegex("^[0-9]{14,19}$")]
    private static partial Regex CardNumberPattern();

    [GeneratedRegex("^[0-9]{3,4}$")]
    private static partial Regex CvvPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Validate, Status={Status}, Fields={Fields}")]
    private partial void LogRejected(string status, IEnumerable<string> fields);
}
