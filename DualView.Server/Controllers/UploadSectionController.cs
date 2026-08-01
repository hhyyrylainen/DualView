using System.ComponentModel.DataAnnotations;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UploadSectionController : Controller
{
    private readonly IDatabaseService databaseService;

    public UploadSectionController(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    [HttpPost("getOrCreate")]
    public async Task<ActionResult<long>> GetOrCreateUploadSection([FromQuery] string? name)
    {
        return (await databaseService.GetOrCreateUploadSectionAsync(name)).Id;
    }

    [HttpPost("{sectionId:long}/addMedia")]
    public async Task<ActionResult> AddMediaToUploadSection([Required] long sectionId, [FromQuery] [Required] long mediaId,
        [FromQuery] [Required] int index)
    {
        await databaseService.AddMediaToUploadSectionAsync(mediaId, sectionId, index);
        return Ok();
    }

    [HttpGet("{sectionId:long}/nextIndex")]
    public async Task<ActionResult<int>> GetNextUploadSectionIndex([Required] long sectionId)
    {
        return await databaseService.GetNextUploadSectionIndexAsync(sectionId);
    }

    [HttpPost("{sectionId:long}/bumpLastImported")]
    public async Task<ActionResult> BumpLastImported([Required] long sectionId)
    {
        await databaseService.BumpUploadSectionLastImportedAsync(sectionId);
        return Ok();
    }
}
