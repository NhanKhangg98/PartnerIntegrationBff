using PartnerIntegration.Core.DTOs;

namespace PartnerIntegration.Core.Interfaces;

public interface ITransactionProcessingService
{
    Task<TransactionIngestionResponseDto> ProcessAsync(PartnerTransactionRequestDto request, CancellationToken ct = default);
}
