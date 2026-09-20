using PaymentGatewayApi.Middleware;
using PaymentGatewayApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var config = new ConfigurationBuilder().AddJsonFile("logconfig.json").Build();
var log = new LoggerConfiguration().ReadFrom.Configuration(config).CreateLogger();
builder.Host.UseSerilog(log);

builder.Services.AddControllers();

builder.Services.AddSingleton<IAwsDynamoFactory, AwsDynamoFactory>();
builder.Services.AddSingleton<IPostPaymentRepository, PostPaymentRepository>();

builder.Services.AddScoped<IPaymentRequestValidator, PaymentRequestValidator>();
builder.Services.AddScoped<IBankHttpClient, BankHttpClient>();
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
