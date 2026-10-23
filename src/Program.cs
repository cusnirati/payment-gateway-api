using System;
using System.Diagnostics;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;
using Polly;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration().WriteTo.Console(new CompactJsonFormatter()).CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddJsonFile("logconfig.json");
    builder.Services.AddSerilog((sp, lc) => lc
        .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(sp));

    // W3C trace context is our correlation id: an incoming traceparent is continued, otherwise a new trace starts,
    // and HttpClient forwards it to the bank. No exporter, the ids end up in the Serilog sinks
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("payment-gateway-api"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation());

    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

    // every error response is an RFC 9457 problem details body (application/problem+json):
    // validation failures, not found, unhandled exceptions (ErrorHandler) and bodiless status codes (UseStatusCodePages)
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        // same 32-hex value as TraceId in the logs, so the caller can hand it over for a lookup
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString();
    });
    builder.Services.AddExceptionHandler<ErrorHandler>();

    // demo setup: the bank call is retried once (2 attempts total) without backoff, only on transient failures
    // (network error, per-attempt timeout, 408, 429, 5xx); ideally fine-tuned with backoff and a circuit breaker
    // HttpClient.Timeout caps the whole call including the retry, so it covers 2 attempts of 10s
    builder.Services.AddHttpClient<IBankHttpClient, BankHttpClient>((serviceProvider, client) =>
        {
            string? bankBaseAddress = serviceProvider.GetRequiredService<IConfiguration>()["SimulatorBankBaseAddress"];
            // on Unix "/payments" parses as an absolute file:// uri, hence the scheme check
            if (!Uri.TryCreate(bankBaseAddress, UriKind.Absolute, out Uri? bankUri)
                || (bankUri.Scheme != Uri.UriSchemeHttp && bankUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"SimulatorBankBaseAddress must be an absolute http or https url, got '{bankBaseAddress}'");
            }

            client.BaseAddress = bankUri;
            client.Timeout = TimeSpan.FromSeconds(25);
        })
        .AddResilienceHandler("bank", pipeline => pipeline
            .AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero })
            .AddTimeout(TimeSpan.FromSeconds(10)));

    var awsOptions = builder.Configuration.GetAWSOptions();
    awsOptions.DefaultClientConfig.Timeout = TimeSpan.FromSeconds(10);
    builder.Services.AddDefaultAWSOptions(awsOptions);
    builder.Services.AddAWSService<IAmazonDynamoDB>();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();

    builder.Services.AddHealthChecks()
        .AddCheck<DynamoDbHealthCheck>("dynamodb", tags: ["ready"]);

    builder.Services.AddScoped<IValidationOrchestrator, ValidationOrchestrator>();
    builder.Services.AddScoped<ICardDetailsValidator, CardDetailsValidator>();
    builder.Services.AddScoped<IPaymentDetailsValidator, PaymentDetailsValidator>();
    builder.Services.AddScoped<IBankResponseProcessor, BankResponseProcessor>();
    builder.Services.AddScoped<IPaymentProcessor, PaymentProcessor>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseMiddleware<LogMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapControllers();

    // curl http://localhost:5067/health/live
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    // curl http://localhost:5067/health/ready
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Operation={Operation}, Status={Status}", "Startup", "Failed");
}
finally
{
    Log.CloseAndFlush();
}
