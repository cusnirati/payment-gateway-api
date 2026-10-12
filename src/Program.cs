using System;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using PaymentGatewayApi.Services.Validators;
using Serilog;
using Serilog.Formatting.Compact;

// set by the official .NET base images; in a container, logs go to stdout as JSON instead of rolling files inside the image
var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

// bootstrap logger covers startup, until AddSerilog replaces it with the logconfig pipeline
Log.Logger = runningInContainer
    ? new LoggerConfiguration().WriteTo.Console(new CompactJsonFormatter()).CreateBootstrapLogger()
    : new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddJsonFile(runningInContainer ? "logconfig.container.json" : "logconfig.json");
    builder.Services.AddSerilog((sp, lc) => lc
        .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(sp));

    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

    // every error response is an RFC 9457 problem details body (application/problem+json):
    // validation failures, not found, unhandled exceptions (ErrorMiddleware) and bodiless status codes (UseStatusCodePages)
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance = context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Response.Headers["CorrelationId"].ToString();
    });

    // using simple httpClient for dev purposes, ideally should be a separate, fine-tuned library with retries, circuit breaker
    builder.Services.AddHttpClient<IBankHttpClient, BankHttpClient>(client => client.Timeout = TimeSpan.FromSeconds(10));

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
    app.UseMiddleware<ErrorMiddleware>();
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
