using System;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("logconfig.json").Build();
var log = new LoggerConfiguration().ReadFrom.Configuration(config).CreateLogger();
builder.Host.UseSerilog(log);

builder.Services.AddControllers();

// a payment POST is not idempotent, a retry after a lost response could charge the card twice;
// keep the timeouts and circuit breaker, but never retry it
builder.Services.AddHttpClient<IBankHttpClient, BankHttpClient>()
    .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

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
app.UseMiddleware<ErrorMiddleware>();

app.MapControllers();
app.Run();

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0#header-propagation-middleware
// https://github.com/dotnet/aspnetcore/issues/48581
// .AddPollyRetries(log);
// app.UseHeaderPropagation();
