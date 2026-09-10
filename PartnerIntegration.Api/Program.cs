using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Http.Resilience;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.Core.Services;
using PartnerIntegration.Infrastructure.Clients;
using Polly;
using System.Net;
using PartnerIntegration.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<PartnerTransactionValidator>();

// Cấu hình Typed HttpClient kèm Resilience Pipeline chuẩn Polly v8
var partnerBaseUrl = builder.Configuration["PartnerService:BaseUrl"] ?? "http://localhost:5000";

builder.Services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>(client =>
{
    client.BaseAddress = new Uri(partnerBaseUrl);
})
.AddResilienceHandler("PartnerVerificationPipeline", pipelineBuilder =>
{
    pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(10));

    pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        // Chỉ retry khi gặp mã lỗi 5xx, RequestTimeout (408) hoặc Exception mạng/Timeout
        ShouldHandle = args =>
        {
            var isServerError = args.Outcome.Result is not null &&
                               ((int)args.Outcome.Result.StatusCode >= 500 ||
                                args.Outcome.Result.StatusCode == HttpStatusCode.RequestTimeout);

            var isTransientException = args.Outcome.Exception is HttpRequestException or TimeoutException;

            return ValueTask.FromResult(isServerError || isTransientException);
        }
    });

    pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(15),
        FailureRatio = 0.5, // 50% lỗi thì mở mạch ngắt request ngay lập tức
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(10)
    });

    pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(2));
});

builder.Services.AddScoped<ITransactionProcessingService, TransactionProcessingService>();
builder.Services.AddRabbitMqMessaging(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();