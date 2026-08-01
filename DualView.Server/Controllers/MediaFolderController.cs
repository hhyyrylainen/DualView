using System.ComponentModel.DataAnnotations;
using Backend.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Requests;
using Backend.Services;
using Backend.Utilities;
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] [Required] CreateFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is missing");

        request.Name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is empty");

        // TODO: add an all-lowercase name to make searching easier

        var parentId = request.ParentFolderId ?? MediaFolder.RootFolderId;

        if (await databaseService.GetMediaFolderAsync(parentId) == null)
            return BadRequest("Parent folder does not exist");

        if (await databaseService.GetMediaFolderAsync(request.Name, parentId) != null)
            return BadRequest("Folder with the same name already exists");

        var id = await databaseService.CreateMediaFolder(request.Name, parentId);

        logger.LogInformation("Created new media folder {Id} with name '{Name}' in folder {FolderId}", id, request.Name,
            parentId);

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

    [HttpGet("deleted")]
    public async Task<ActionResult<List<MediaFolderDTO>>> GetDeleted([FromQuery] int limit = 100)
    {
        return (await databaseService.GetDeletedMediaFoldersAsync(limit)).ConvertToDTO<MediaFolder, MediaFolderDTO>();
    }

    [HttpPost("{id:long}/restore")]
    public async Task<IActionResult> Restore([Required] long id)
    {
        await databaseService.RestoreMediaFolderAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/purge")]
    public async Task<IActionResult> Purge([Required] long id)
    {
        await databaseService.PurgeMediaFolderAsync(id);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete([Required] long id)
    {
        await databaseService.DeleteMediaFolderAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/addToFolder/{parentFolderId:long}")]
    public async Task<IActionResult> AddToFolder([Required] long id, [Required] long parentFolderId)
    {
        await databaseService.AddFolderToFolder(id, parentFolderId);
        return Ok();
    }

    [HttpDelete("{id:long}/removeFromFolder/{parentFolderId:long}")]
    public async Task<IActionResult> RemoveFromFolder([Required] long id, [Required] long parentFolderId)
    {
        await databaseService.RemoveFolderFromFolder(id, parentFolderId);
        return Ok();
    }

    [HttpGet("{id:long}/parentFolderPaths")]
    public async Task<ActionResult<List<FolderPathDTO>>> GetParentFolderPaths([Required] long id)
    {
        return await databaseService.GetFolderParentFolderPaths(id);
    }

    [HttpGet("{id:long}/path")]
    public async Task<ActionResult<string>> GetPath([Required] long id)
    {
        return await databaseService.GetMediaFolderPath(id);
    }
}
