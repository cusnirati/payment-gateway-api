using System;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Health;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Security;
using PaymentGatewayApi.Services;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;

// catches what goes wrong before the configured logger exists, e.g. an invalid setting
Log.Logger = new LoggerConfiguration().WriteTo.Console(new RenderedCompactJsonFormatter()).CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // sinks, levels and formats come from the "Serilog" section: JSON to the console by default,
    // readable console plus files under z-logs/ in Development; ReadFrom.Services lets tests add a sink.
    // The host owns (and flushes) this logger; the static Log.Logger stays the startup logger above.
    builder.Services.AddSerilog(
        (services, logger) => logger.ReadFrom.Configuration(builder.Configuration).ReadFrom.Services(services),
        preserveStaticLogger: true);

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // statuses read as names, and a null where the model says non-nullable is a 400, not a crash later
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.RespectNullableAnnotations = true;
        });

    // every error body is a ProblemDetails with the request's traceId and correlationId, never an exception message
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        if (CorrelationIdMiddleware.Get(context.HttpContext) is { } correlationId)
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // live: the process answers; ready: it can also reach its table (tagged checks only)
    builder.Services.AddHealthChecks()
        .AddCheck<DynamoHealthCheck>("dynamodb", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

    // a missing or malformed setting fails the startup, not the first request that needs it
    builder.Services.AddOptions<BankOptions>()
        .BindConfiguration(BankOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<PaymentOptions>()
        .BindConfiguration(PaymentOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<DynamoOptions>()
        .BindConfiguration(DynamoOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddOptions<ApiKeyOptions>()
        .BindConfiguration(ApiKeyOptions.SectionName)
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();
    builder.Services.AddOptions<RateLimitOptions>()
        .BindConfiguration(RateLimitOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton(TimeProvider.System);

    // merchants authenticate with an API key; every endpoint requires it unless it opts out
    // (health checks, the development OpenAPI document), so a new endpoint is never public by accident
    builder.Services.AddSingleton<IMerchantApiKeys, MerchantApiKeys>();
    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    builder.Services.AddPaymentRateLimiting();

    // a payment POST is not idempotent, a retry after a lost response could charge the card twice;
    // keep the timeouts and circuit breaker (tunable under Bank:Resilience), but never retry it
    builder.Services.AddHttpClient<IBankClient, BankClient>((services, client) =>
            client.BaseAddress = services.GetRequiredService<IOptions<BankOptions>>().Value.BaseUrl)
        .AddStandardResilienceHandler(builder.Configuration.GetSection("Bank:Resilience"))
        .Configure(options => options.Retry.DisableForUnsafeHttpMethods());

    var awsOptions = builder.Configuration.GetAWSOptions();
    awsOptions.DefaultClientConfig.Timeout = TimeSpan.FromSeconds(10);
    builder.Services.AddDefaultAWSOptions(awsOptions);
    builder.Services.AddAWSService<IAmazonDynamoDB>();

    // stateless, so shared; the payment service is scoped because it holds the typed bank client,
    // which must not be captured by a singleton (its handler is rotated by IHttpClientFactory)
    builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
    builder.Services.AddSingleton<IPaymentRequestValidator, PaymentRequestValidator>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();

    // the platform's own OpenAPI document generator, served at /openapi/v1.json
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // the correlation id wraps everything else, so every log line of the request carries it,
    // including the request summary and the exception handler's
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, context) =>
        diagnostics.Set("MerchantId", context.User.FindMerchantId()));
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        // the API description and its UI are a development aid, not part of the deployed surface
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Payment Gateway API"));
    }
    else
    {
        // redirects only once an HTTPS port is configured; behind a TLS-terminating load balancer
        // that also needs forwarded headers, see the README
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // every log line after authentication names the merchant
    app.Use(async (context, next) =>
    {
        using (LogContext.PushProperty("MerchantId", context.User.FindMerchantId()))
        {
            await next(context);
        }
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
    app.MapControllers();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
    }

    app.Run();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // HostAbortedException is how tooling such as WebApplicationFactory stops the host on purpose
    Log.Fatal(exception, "Startup, Status={Status}", "Failed");
    return 1;
}
finally
{
    // the file sink buffers, flush it before the process exits
    await Log.CloseAndFlushAsync();
}
