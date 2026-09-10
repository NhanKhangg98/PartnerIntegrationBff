using Microsoft.AspNetCore.Mvc;

namespace PartnerIntegration.Api.Controllers;

/// <summary>
/// Mock endpoint simulating a downstream partner verification service.
/// Purpose: Used for local development and testing only.
/// Behavior: 30% probability of simulating a slow/timeout response; 70% returns valid.
/// NOTE: This controller should be disabled or removed in a real production environment.
/// </summary>
[ApiController]
[Route("api/internal/mock/partners")]
public class MockPartnerController : ControllerBase
{
    private static readonly Random _random = Random.Shared;

    private const double TimeoutProbability = 0.30;

    private static readonly TimeSpan SimulatedTimeoutDelay = TimeSpan.FromSeconds(5);

    [HttpGet("{partnerId}/verify")]
    public async Task<IActionResult> VerifyPartner(string partnerId, CancellationToken cancellationToken)
    {
        if (_random.NextDouble() < TimeoutProbability)
        {
            await Task.Delay(SimulatedTimeoutDelay, cancellationToken);
        }

        return Ok(new
        {
            partnerId,
            isValid = true,
            verifiedAt = DateTime.UtcNow
        });
    }
}
