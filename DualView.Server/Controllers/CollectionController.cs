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
    private readonly ICollectionSimilarityService collectionSimilarityService;

    public CollectionController(IDatabaseService databaseService,
        ICollectionSimilarityService collectionSimilarityService)
    {
        this.databaseService = databaseService;
        this.collectionSimilarityService = collectionSimilarityService;
    }

    [HttpPost("{id:long}/sortByVisualSimilarity")]
    public async Task<ActionResult<long>> SortByVisualSimilarity([Required] long id)
    {
        return await collectionSimilarityService.StartSortByVisualSimilarity(id);
    }

    [HttpGet("sortByVisualSimilarity/{operationId:long}")]
    public ActionResult<List<long>> GetVisualSimilarityOrder([Required] long operationId)
    {
        try
        {
            return collectionSimilarityService.GetVisualSimilarityOrder(operationId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CollectionDTO?>> GetById([Required] long id)
    {
        var collection = await databaseService.GetCollectionAsync(id);
        return collection?.GetDTO();
    }

    [HttpPut("{id:long}/rename")]
    public async Task<IActionResult> Rename([Required] long id, [FromBody] [Required] string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Name is missing");
        if (name.Length > 200)
            return BadRequest("Collection name is too long");

        await databaseService.RenameCollection(id, name);
        return Ok();
    }

    [HttpPut("{id:long}/imageGroupSize")]
    public async Task<IActionResult> SetImageGroupSize([Required] long id, [FromBody] int imageGroupSize)
    {
        await databaseService.SetCollectionImageGroupSizeAsync(id, imageGroupSize);
        return Ok();
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

    [HttpGet("{id:long}/orphanedMediaCount")]
    public async Task<ActionResult<int>> GetOrphanedMediaCount([Required] long id)
    {
        return await databaseService.GetCollectionOrphanedMediaCountAsync(id);
    }

    [HttpPost("{id:long}/deleteAndImages")]
    public async Task<ActionResult<CollectionMediaRemovalResult>> DeleteAndImages([Required] long id)
    {
        return await databaseService.DeleteCollectionAndImagesAsync(id);
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
        [Required] int page, int pageSize = 100, CollectionSortColumn sortColumn = CollectionSortColumn.CollectionOrder,
        SortDirection sortDirection = SortDirection.Ascending, string? search = null)
    {
        return await databaseService.GetCollectionContents(id, page, pageSize, sortColumn, sortDirection, search);
    }

    [HttpGet("{id:long}/allContents")]
    public async Task<ActionResult<List<MediaFileDTO>>> GetAllCollectionContents([Required] long id)
    {
        return (await databaseService.GetCollectionContents(id)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    [HttpGet("{id:long}/browseInfo")]
    public async Task<ActionResult<CollectionBrowseInfoDTO>> GetCollectionBrowseInfo([Required] long id,
        long? mediaId = null)
    {
        return await databaseService.GetCollectionBrowseInfoAsync(id, mediaId);
    }

    [HttpGet("{id:long}/browse/{index:int}")]
    public async Task<ActionResult<MediaFileDTO>> GetCollectionMediaAtIndex([Required] long id, int index)
    {
        var media = await databaseService.GetCollectionMediaAtIndexAsync(id, index);
        return media == null ? NotFound() : media;
    }

    [HttpPost("{id:long}/reorder")]
    public async Task<IActionResult> Reorder([Required] long id, [FromBody] List<long> newImageOrderIds)
    {
        await databaseService.ReorderCollection(id, newImageOrderIds);
        return Ok();
    }

    [HttpPost("{id:long}/addMedia")]
    public async Task<IActionResult> AddMediaToCollection([Required] long id,
        [FromBody] CollectionMediaImportRequest request)
    {
        await databaseService.AddMediaToCollection(request.MediaIds, id, request.FirstSequenceNumber,
            request.SequenceNumbers);
        return Ok();
    }

    [HttpPost("{id:long}/removeMedia")]
    public async Task<IActionResult> RemoveMediaFromCollection([Required] long id,
        [Required] long mediaId)
    {
        try
        {
            await databaseService.RemoveMediaFromCollection(mediaId, id);
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

    [HttpPost("{id:long}/previewRemoveMedia")]
    public async Task<ActionResult<CollectionMediaRemovalPreview>> PreviewRemoveMedia([Required] long id,
        [FromBody] List<long> mediaIds)
    {
        return await databaseService.PreviewCollectionMediaRemovalAsync(id, mediaIds);
    }

    [HttpPost("{id:long}/removeSelectedMedia")]
    public async Task<ActionResult<CollectionMediaRemovalResult>> RemoveSelectedMedia([Required] long id,
        [FromBody] List<long> mediaIds)
    {
        try
        {
            return await databaseService.RemoveMediaFromCollectionAsync(id, mediaIds);
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

    [HttpPost("undoRemoveMedia")]
    public async Task<IActionResult> UndoRemoveMedia([FromBody] CollectionMediaRemovalResult removal)
    {
        await databaseService.UndoCollectionMediaRemovalAsync(removal);
        return Ok();
    }

    [HttpPost("{id:long}/appliedTag")]
    public async Task<ActionResult<long>> AddAppliedTag([Required] long id, [FromBody] AddAppliedTagRequest request)
    {
        if (request.ParsedTag != null)
            return Ok(await databaseService.AddParsedAppliedTagToCollectionAsync(id, request.ParsedTag));

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
