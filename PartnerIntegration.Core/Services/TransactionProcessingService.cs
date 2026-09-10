using Microsoft.Extensions.Logging;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;

namespace PartnerIntegration.Core.Services;

public class TransactionProcessingService : ITransactionProcessingService
{
    private const string LegacyTransactionQueue = "legacy_transactions_queue";

    private readonly IPartnerVerificationClient _verifierClient;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ILogger<TransactionProcessingService> _logger;

    public TransactionProcessingService(
        IPartnerVerificationClient verifierClient,
        IMessagePublisher messagePublisher,
        ILogger<TransactionProcessingService> logger)
    {
        _verifierClient = verifierClient;
        _messagePublisher = messagePublisher;
        _logger = logger;
    }

    public async Task<TransactionIngestionResponseDto> ProcessAsync(PartnerTransactionRequestDto request, CancellationToken ct = default)
    {
        bool isValidPartner;
        try
        {
            isValidPartner = await _verifierClient.VerifyPartnerAsync(request.PartnerId!, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            _logger.LogError(ex, "Downstream verification service unreachable for partner {PartnerId}", request.PartnerId);

            return new TransactionIngestionResponseDto(
                IsSuccess: false,
                StatusCode: 503,
                ResponseBody: new
                {
                    error = "Dependency Failure",
                    message = "Partner verification service is currently unreachable."
                }
            );
        }

        if (!isValidPartner)
        {
            return new TransactionIngestionResponseDto(
                IsSuccess: false,
                StatusCode: 422,
                ResponseBody: new
                {
                    error = "Invalid Partner",
                    message = $"Partner ID '{request.PartnerId}' is either inactive or not found."
                }
            );
        }

        await _messagePublisher.PublishAsync(LegacyTransactionQueue, request, ct);

        return new TransactionIngestionResponseDto(
            IsSuccess: true,
            StatusCode: 202,
            ResponseBody: new
            {
                message = "Transaction successfully queued for ingestion.",
                transactionReference = request.TransactionReference,
                enqueuedAt = DateTime.UtcNow
            }
        );
    }
}