using Backend.Services;
using DualView.Shared.Models.DTO;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/remoteScan")]
public class RemoteScanController : Controller
{
    private readonly IRemoteScanService remoteScanService;

    public RemoteScanController(IRemoteScanService remoteScanService)
    {
        this.remoteScanService = remoteScanService;
    }

    [HttpGet("events")]
    public async Task<IReadOnlyList<RemoteScanEventDTO>> GetEvents(CancellationToken cancellationToken)
    {
        var events = await remoteScanService.GetEventsAsync(cancellationToken);
        return events.Select(scanEvent => new RemoteScanEventDTO
        {
            CreatedAt = scanEvent.CreatedAt,
            EventType = scanEvent.EventType,
            MainUrl = scanEvent.ImageUrl,
            ReferrerUrl = scanEvent.ReferrerUrl,
            Detail = scanEvent.Detail,
            Attempt = scanEvent.Attempt,
        }).ToList();
    }

    [HttpPost("events/clear")]
    public async Task<IActionResult> ClearEvents(CancellationToken cancellationToken)
    {
        await remoteScanService.ClearEventsAsync(cancellationToken);
        return Ok();
    }
}
