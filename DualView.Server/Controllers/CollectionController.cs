using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Requests;
using Backend.Models;
using Backend.Services;
using Backend.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CollectionController : Controller
{
    private readonly IDatabaseService databaseService;

    public CollectionController(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CollectionDTO?>> GetById([Required] long id)
    {
        var collection = await databaseService.GetCollectionAsync(id);
        return collection?.GetDTO();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<List<CollectionDTO>>> GetDeleted([FromQuery] int limit = 100)
    {
        return (await databaseService.GetDeletedCollectionsAsync(limit)).ConvertToDTO<Collection, CollectionDTO>();
    }

    [HttpPost("{id:long}/restore")]
    public async Task<IActionResult> Restore([Required] long id)
    {
        await databaseService.RestoreCollectionAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/purge")]
    public async Task<IActionResult> Purge([Required] long id)
    {
        await databaseService.PurgeCollectionAsync(id);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete([Required] long id)
    {
        await databaseService.DeleteCollectionAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/addToFolder/{folderId:long}")]
    public async Task<IActionResult> AddToFolder([Required] long id, [Required] long folderId)
    {
        await databaseService.AddCollectionToFolder(id, folderId);
        return Ok();
    }

    [HttpDelete("{id:long}/removeFromFolder/{folderId:long}")]
    public async Task<IActionResult> RemoveFromFolder([Required] long id, [Required] long folderId)
    {
        await databaseService.RemoveCollectionFromFolder(id, folderId);
        return Ok();
    }

    [HttpGet("{id:long}/folderPaths")]
    public async Task<ActionResult<List<FolderPathDTO>>> GetFolderPaths([Required] long id)
    {
        return await databaseService.GetCollectionFolderPaths(id);
    }

    [HttpGet("{id:long}/contents")]
    public async Task<ActionResult<Tuple<List<MediaFileDTO>, int>>> GetCollectionContents([Required] long id,
        [Required] int page, int pageSize = 100, FolderSortColumn sortColumn = FolderSortColumn.DateCreated,
        SortDirection sortDirection = SortDirection.Descending)
    {
        return await databaseService.GetCollectionContents(id, page, pageSize, sortColumn, sortDirection);
    }

    [HttpGet("{id:long}/allContents")]
    public async Task<ActionResult<List<MediaFileDTO>>> GetAllCollectionContents([Required] long id)
    {
        return (await databaseService.GetCollectionContents(id)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    [HttpPost("{id:long}/reorder")]
    public async Task<IActionResult> Reorder([Required] long id, [FromBody] List<long> newImageOrderIds)
    {
        await databaseService.ReorderCollection(id, newImageOrderIds);
        return Ok();
    }

    [HttpPost("{id:long}/addMedia")]
    public async Task<IActionResult> AddMediaToCollection([Required] long id,
        [Required] long mediaId, [Required] int sequenceNumber)
    {
        await databaseService.AddMediaToCollection(mediaId, id, sequenceNumber);
        return Ok();
    }

    [HttpPost("{id:long}/removeMedia")]
    public async Task<IActionResult> RemoveMediaFromCollection([Required] long id,
        [Required] long mediaId)
    {
        await databaseService.RemoveMediaFromCollection(mediaId, id);
        return Ok();
    }

    [HttpPost("{id:long}/appliedTag")]
    public async Task<ActionResult<long>> AddAppliedTag([Required] long id, [FromBody] AddAppliedTagRequest request)
    {
        var appliedTagId = await databaseService.AddAppliedTagToCollectionAsync(id, request.TagId, request.ModifierIds,
            request.CombinedWithAppliedTagId, request.CombineWord);
        return Ok(appliedTagId);
    }

    [HttpDelete("{id:long}/appliedTag/{appliedTagId:long}")]
    public async Task<IActionResult> RemoveAppliedTag([Required] long id, [Required] long appliedTagId)
    {
        await databaseService.RemoveAppliedTagFromCollectionAsync(id, appliedTagId);
        return Ok();
    }

    [HttpGet("{id:long}/appliedTag")]
    public async Task<ActionResult<List<AppliedTagDTO>>> GetAppliedTags([Required] long id)
    {
        return (await databaseService.GetCollectionAppliedTagsAsync(id)).ConvertToDTO<AppliedTag, AppliedTagDTO>();
    }
}
