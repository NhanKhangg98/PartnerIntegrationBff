namespace PartnerIntegration.Core.DTOs;

public record PartnerTransactionRequestDto(
    string? PartnerId,
    string? TransactionReference,
    decimal? Amount,
    string? Currency,
    DateTime? Timestamp
);
