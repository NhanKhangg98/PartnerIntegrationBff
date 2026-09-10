using Microsoft.AspNetCore.Mvc;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;

namespace PartnerIntegration.Api.Controllers;

[ApiController]
[Route("api/v1/partner/transactions")]
public class PartnerTransactionsController : ControllerBase
{
    private readonly ITransactionProcessingService _service;

    public PartnerTransactionsController(ITransactionProcessingService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> IngestTransaction([FromBody] PartnerTransactionRequestDto request, CancellationToken ct)
    {
        var result = await _service.ProcessAsync(request, ct);
        return StatusCode(result.StatusCode, result.ResponseBody);
    }
}