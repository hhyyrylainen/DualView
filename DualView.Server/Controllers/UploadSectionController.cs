using System.ComponentModel.DataAnnotations;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using DualView.Shared.Requests;
using DualView.Shared.Models.DTO;

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

    [HttpGet]
    public async Task<ActionResult<List<UploadSectionDTO>>> GetAll()
    {
        var sections = await databaseService.GetUploadSectionsAsync();
        return sections.Select(section => section.GetDTO()).ToList();
    }

    [HttpGet("recent")]
    public async Task<ActionResult<List<RecentImportSectionDTO>>> GetRecent()
    {
        var sections = await databaseService.GetRecentImportSectionsAsync();
        return sections.Select(section => new RecentImportSectionDTO
        {
            Name = section.Name,
            LastUsed = section.LastUsed,
        }).ToList();
    }

    [HttpGet("{sectionId:long}")]
    public async Task<ActionResult<UploadSectionDTO>> Get([FromRoute] long sectionId)
    {
        var section = await databaseService.GetUploadSectionAsync(sectionId);
        if (section == null)
            return NotFound();

        return section.GetDTO();
    }

    [HttpGet("targetNames")]
    public async Task<ActionResult<List<string>>> GetTargetNames([FromQuery] string search, [FromQuery] int limit = 100)
    {
        if (search.Trim().Length <= 2)
            return new List<string>();

        return await databaseService.SearchUploadTargetNamesAsync(search, limit);
    }

    [HttpPut("{sectionId:long}")]
    public async Task<ActionResult> Update(long sectionId, UploadSectionDTO request)
    {
        var section = await databaseService.GetUploadSectionAsync(sectionId);
        if (section == null) return NotFound();
        section.Name = request.Name.Trim();
        section.KeepTarget = request.KeepTarget;
        section.RemoveAfterImport = request.RemoveAfterImport;
        section.TargetFolderId = request.TargetFolderId;
        await databaseService.SaveUploadSectionAsync(section);
        return Ok();
    }

    [HttpDelete("{sectionId:long}")]
    public async Task<ActionResult> Delete(long sectionId)
    {
        if (await databaseService.GetUploadSectionAsync(sectionId) == null)
            return NotFound();

        await databaseService.DeleteUploadSectionAsync(sectionId);
        return Ok();
    }

    [HttpPost("active")]
    public async Task<ActionResult> SetActive([FromBody] long? sectionId)
    {
        await databaseService.SetUploadSectionActiveAsync(sectionId);
        return Ok();
    }

    [HttpPost("active/addMedia")]
    public async Task<ActionResult> AddMediaToActive([FromBody] List<long> mediaIds)
    {
        await databaseService.AddMediaToActiveUploadSectionAsync(mediaIds);
        return Ok();
    }

    [HttpPost("{sectionId:long}/removeMedia")]
    public async Task<ActionResult> RemoveMedia(long sectionId, UploadSectionMediaRequest request)
    {
        await databaseService.RemoveMediaFromUploadSectionAsync(sectionId, request.MediaIds);
        return Ok();
    }

    [HttpPost("{sectionId:long}/reorder")]
    public async Task<ActionResult> Reorder(long sectionId, UploadSectionMediaRequest request)
    {
        await databaseService.ReorderUploadSectionAsync(sectionId, request.MediaIds);
        return Ok();
    }

    [HttpPost("{sectionId:long}/import")]
    public async Task<ActionResult> Import(long sectionId, [FromBody] List<long>? mediaIds)
    {
        await databaseService.ImportUploadSectionAsync(sectionId, mediaIds);
        return Ok();
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
