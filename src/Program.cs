using System;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// bootstrap logger covers startup, until AddSerilog replaces it with the logconfig.json pipeline
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddJsonFile("logconfig.json");
    builder.Services.AddSerilog((sp, lc) => lc
        .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(sp));

    builder.Services.AddControllers();

    // using simple httpClient for dev purpose, ideally should be a separate, fine tuned library for retries, circuit breaker
    builder.Services.AddHttpClient<IBankHttpClient, BankHttpClient>();

    var awsOptions = builder.Configuration.GetAWSOptions();
    awsOptions.DefaultClientConfig.Timeout = TimeSpan.FromSeconds(10);
    builder.Services.AddDefaultAWSOptions(awsOptions);
    builder.Services.AddAWSService<IAmazonDynamoDB>();

    builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();

    builder.Services.AddScoped<IPaymentRequestValidator, PaymentRequestValidator>();
    builder.Services.AddScoped<IBankResponseProcessor, BankResponseProcessor>();
    builder.Services.AddScoped<IBankService, BankService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseMiddleware<LogMiddleware>();
    // after LogMiddleware so the completion event carries CallerId/CorrelationId, before ErrorMiddleware so it sees the final 500
    app.UseSerilogRequestLogging();
    app.UseMiddleware<ErrorMiddleware>();

    app.MapControllers();
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

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0#header-propagation-middleware
// https://github.com/dotnet/aspnetcore/issues/48581
// .AddPollyRetries(log);
// app.UseHeaderPropagation();
// builder.Services.ConfigureHttpClientDefaults(http =>
// {
//     http.AddStandardResilienceHandler();
// });
