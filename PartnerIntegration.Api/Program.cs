using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using PartnerIntegration.Api.Middlewares;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.Core.Services;
using PartnerIntegration.Core.Validators;
using PartnerIntegration.Infrastructure.Clients;
using PartnerIntegration.Infrastructure.DependencyInjection;
using Polly;
using Polly.Timeout;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<PartnerTransactionValidator>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));

    options.AddFixedWindowLimiter("transactions", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 5;
    });
});

var partnerBaseUrl = builder.Configuration["PartnerService:BaseUrl"] ?? "http://localhost:5000";

builder.Services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>(client =>
{
    client.BaseAddress = new Uri(partnerBaseUrl);
})
.AddResilienceHandler("PartnerVerificationPipeline", pipelineBuilder =>
{
    pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(30));

    pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = args =>
        {
            var isServerError = args.Outcome.Result is not null &&
                                ((int)args.Outcome.Result.StatusCode >= 500 ||
                                 args.Outcome.Result.StatusCode == HttpStatusCode.RequestTimeout);

            var isTransientException = args.Outcome.Exception is
                HttpRequestException or
                TimeoutException or
                TimeoutRejectedException;

            return ValueTask.FromResult(isServerError || isTransientException);
        }
    });

    pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(15),
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(10)
    });

    pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(3));
});

builder.Services.AddScoped<ITransactionProcessingService, TransactionProcessingService>();
builder.Services.AddRabbitMqMessaging(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();

app.UseMiddleware<ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers()
   .RequireRateLimiting("transactions");

app.Run();

public partial class Program { }