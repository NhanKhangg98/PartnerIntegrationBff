namespace PartnerIntegration.Core.Interfaces;

public interface IPartnerVerificationClient
{
    Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken ct = default);
}
