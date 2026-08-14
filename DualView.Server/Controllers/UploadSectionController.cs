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
        return sections.Select(section => new UploadSectionDTO
        {
            Id = section.Id, Name = section.Name, KeepTarget = section.KeepTarget,
            Selected = section.Selected, RemoveAfterImport = section.RemoveAfterImport,
            TargetFolderId = section.TargetFolderId,
            TargetCollectionName = string.IsNullOrWhiteSpace(section.TargetCollectionName)
                ? section.Name
                : section.TargetCollectionName,
            Media = section.Items.OrderBy(item => item.Index).Select(item => item.MediaFile.GetDTO()).ToList(),
        }).ToList();
    }

    [HttpPut("{sectionId:long}")]
    public async Task<ActionResult> Update(long sectionId, UpdateUploadSectionRequest request)
    {
        var section = (await databaseService.GetUploadSectionsAsync()).FirstOrDefault(item => item.Id == sectionId);
        if (section == null) return NotFound();
        section.Name = request.Name.Trim();
        section.KeepTarget = request.KeepTarget;
        section.RemoveAfterImport = request.RemoveAfterImport;
        section.TargetFolderId = request.TargetFolderId;
        section.TargetCollectionName = request.TargetCollectionName;
        await databaseService.SaveUploadSectionAsync(section);
        return Ok();
    }

    [HttpPost("active")]
    public async Task<ActionResult> SetActive([FromBody] long? sectionId)
    {
        await databaseService.SetUploadSectionActiveAsync(sectionId);
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
