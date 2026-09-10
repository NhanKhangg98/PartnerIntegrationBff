using PartnerIntegration.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PartnerIntegration.Infrastructure.Clients;

public class PartnerVerificationClient : IPartnerVerificationClient
{
    private readonly HttpClient _httpClient;

    public PartnerVerificationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> VerifyPartnerAsync(string partnerId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/internal/mock/partners/{partnerId}/verify", ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();

        return true;
    }
}
