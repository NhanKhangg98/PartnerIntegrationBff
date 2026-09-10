namespace PartnerIntegration.Core.DTOs;

public record TransactionIngestionResponseDto(
    bool IsSuccess,
    int StatusCode,
    object ResponseBody
);
