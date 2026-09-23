using System;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PaymentGatewayApi.Configuration;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("logconfig.json").Build();
var log = new LoggerConfiguration().ReadFrom.Configuration(config).CreateLogger();
builder.Host.UseSerilog(log);

builder.Services.AddControllers();

// a missing or malformed setting fails the startup, not the first request that needs it
builder.Services.AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DynamoOptions>()
    .BindConfiguration(DynamoOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<LogMiddleware>();
app.UseMiddleware<ErrorMiddleware>();

app.MapControllers();
app.Run();

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0#header-propagation-middleware
// https://github.com/dotnet/aspnetcore/issues/48581
// .AddPollyRetries(log);
// app.UseHeaderPropagation();
