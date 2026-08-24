using Backend.Services;
using DualView.Shared.Models.DTO;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/missingTag")]
public class MissingTagController : Controller
{
    private readonly IMissingTagService missingTagService;

    public MissingTagController(IMissingTagService missingTagService)
    {
        this.missingTagService = missingTagService;
    }

    [HttpGet]
    public Task<List<MissingTagDTO>> Get()
    {
        return missingTagService.GetMissingTagsAsync();
    }

    [HttpPost("ignore")]
    public async Task<IActionResult> Ignore([FromBody] string tag)
    {
        await missingTagService.IgnoreTagAsync(tag);
        return Ok();
    }

    [HttpPost("clearCurrent")]
    public async Task<IActionResult> ClearCurrent()
    {
        await missingTagService.ClearCurrentDetectionsAsync();
        return Ok();
    }

    [HttpPost("resetIgnored")]
    public async Task<IActionResult> ResetIgnored()
    {
        await missingTagService.ResetIgnoredTagsAsync();
        return Ok();
    }

    [HttpPost("report")]
    public async Task<IActionResult> Report([FromBody] MissingTagDTO request)
    {
        await missingTagService.ReportTagAsync(request.Tag, request.Target, request.TargetId);
        return Ok();
    }
}
