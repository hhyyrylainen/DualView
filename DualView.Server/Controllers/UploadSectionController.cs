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
    private readonly IImportSectionSimilarityService importSectionSimilarityService;

    public UploadSectionController(IDatabaseService databaseService,
        IImportSectionSimilarityService importSectionSimilarityService)
    {
        this.databaseService = databaseService;
        this.importSectionSimilarityService = importSectionSimilarityService;
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
        return await databaseService.GetRecentImportSectionsAsync();
    }

    [HttpGet("{sectionId:long}")]
    public async Task<ActionResult<UploadSectionDTO>> Get([FromRoute] long sectionId)
    {
        var section = await databaseService.GetUploadSectionAsync(sectionId);
        if (section == null)
            return NotFound();

        return section.GetDTO();
    }

    [HttpGet("{sectionId:long}/appliedTag")]
    public async Task<ActionResult<List<AppliedTagDTO>>> GetAppliedTags(long sectionId)
    {
        return (await databaseService.GetUploadSectionAppliedTagsAsync(sectionId))
            .Select(tag => tag.GetDTO()).ToList();
    }

    [HttpPost("{sectionId:long}/appliedTag")]
    public async Task<ActionResult<long>> AddAppliedTag(long sectionId, AddAppliedTagRequest request)
    {
        return await databaseService.AddAppliedTagToUploadSectionAsync(sectionId, request.TagId, request.ModifierIds,
            request.CombinedWithAppliedTagId, request.CombineWord);
    }

    [HttpDelete("{sectionId:long}/appliedTag/{appliedTagId:long}")]
    public async Task<IActionResult> RemoveAppliedTag(long sectionId, long appliedTagId)
    {
        await databaseService.RemoveAppliedTagFromUploadSectionAsync(sectionId, appliedTagId);
        return Ok();
    }

    [HttpPost("{sectionId:long}/sortByVisualSimilarity")]
    public async Task<ActionResult<long>> SortByVisualSimilarity([Required] long sectionId)
    {
        return await importSectionSimilarityService.StartSortByVisualSimilarity(sectionId);
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
        try
        {
            await databaseService.ImportUploadSectionAsync(sectionId, mediaIds);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
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
