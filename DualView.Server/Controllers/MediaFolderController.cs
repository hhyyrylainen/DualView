using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Requests;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MediaFolderController : Controller
{
    private readonly ILogger<MediaFolderController> logger;
    private readonly IDatabaseService databaseService;

    public MediaFolderController(ILogger<MediaFolderController> logger, IDatabaseService databaseService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
    }

    [HttpGet]
    public async Task<ActionResult<List<MediaFolderInfo>>> GetAllFolders(long? parentFolderId = null)
    {
        var folders = await databaseService.GetMediaFoldersAsync(parentFolderId);
        return folders.Select(f => f.GetInfo()).ToList();
    }

    [HttpGet("{folderId:long}/collections")]
    public async Task<ActionResult<Tuple<List<CollectionDTO>, int>>> GetFolderCollections([Required] long folderId,
        [Required] int page, int pageSize = 100)
    {
        return await databaseService.GetFolderCollections(folderId, page, pageSize);
    }

    [HttpGet("collection/{collectionId:long}/contents")]
    public async Task<ActionResult<Tuple<List<MediaFileDTO>, int>>> GetCollectionContents([Required] long collectionId,
        [Required] int page, int pageSize = 100, FolderSortColumn sortColumn = FolderSortColumn.DateCreated,
        SortDirection sortDirection = SortDirection.Descending)
    {
        return await databaseService.GetCollectionContents(collectionId, page, pageSize, sortColumn, sortDirection);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] [Required] CreateFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is missing");

        request.Name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is empty");

        // TODO: add an all-lowercase name to make searching easier

        if (request.ParentFolderId != null)
        {
            if (await databaseService.GetMediaFolderAsync(request.ParentFolderId.Value) == null)
                return BadRequest("Parent folder does not exist");
        }

        if (await databaseService.GetMediaFolderAsync(request.Name, request.ParentFolderId) != null)
            return BadRequest("Folder with the same name already exists");

        var id = await databaseService.CreateMediaFolder(request.Name, request.ParentFolderId);

        logger.LogInformation("Created new media folder {Id} with name '{Name}' in folder {FolderId}", id, request.Name,
            request.ParentFolderId);

        return Ok(id.ToString());
    }

    // TODO: should split out a separate collections controller
    [HttpPost("{folderId:long}/collections")]
    public async Task<IActionResult> CreateCollection([Required] long folderId, [FromBody] [Required] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Name is missing");

        var id = await databaseService.CreateCollection(name, folderId);
        return Ok(id.ToString());
    }

    [HttpGet("atPath")]
    public async Task<ActionResult<MediaFolderDTO?>> GetByPath(string path)
    {
        var media = await databaseService.GetMediaFolderFromPathAsync(path);

        if (media == null)
            return Json(null);

        return media.GetDTO();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<MediaFolderDTO?>> GetById([Required] long id)
    {
        var folder = await databaseService.GetMediaFolderAsync(id);

        if (folder == null)
            return Json(null);

        return folder.GetDTO();
    }

    [HttpPost("collection/{collectionId:long}/addMedia")]
    public async Task<IActionResult> AddMediaToCollection([Required] long collectionId,
        [Required] long mediaId, [Required] int sequenceNumber)
    {
        await databaseService.AddMediaToCollection(mediaId, collectionId, sequenceNumber);
        return Ok();
    }

    [HttpPost("collection/{collectionId:long}/removeMedia")]
    public async Task<IActionResult> RemoveMediaFromCollection([Required] long collectionId,
        [Required] long mediaId)
    {
        await databaseService.RemoveMediaFromCollection(mediaId, collectionId);
        return Ok();
    }
}
