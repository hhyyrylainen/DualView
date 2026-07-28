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
    public async Task<ActionResult<List<MediaStorageFolderInfo>>> GetAllFolders(long? parentFolderId = null)
    {
        var media = await databaseService.GetMediaFoldersAsync(parentFolderId);

        return media.ConvertToInfo<MediaStorageFolder, MediaStorageFolderInfo>();
    }

    [HttpGet("folder/full/{folderId:long}")]
    public async Task<ActionResult<List<ConfiguredMediaDTO>>> GetFullMediaList([Required] long folderId)
    {
        return (await databaseService.GetMediaInFolderAsync(folderId))
            .ConvertToDTO<ConfiguredMedia, ConfiguredMediaDTO>();
    }

    [HttpGet("folder/{folderId:long}")]
    public async Task<ActionResult<Tuple<List<ConfiguredMediaInfo>, int>>> GetMediaPaged([Required] long folderId,
        [Required] int page, int pageSize = 100, FolderSortColumn sortColumn = FolderSortColumn.DateCreated,
        SortDirection sortDirection = SortDirection.Descending)
    {
        return await databaseService.GetMediaFolderContents(folderId, page, pageSize, sortColumn, sortDirection);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] [Required] CreateFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is missing");

        request.Name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is empty");

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

    [HttpGet("atPath")]
    public async Task<ActionResult<MediaStorageFolderDTO?>> GetByPath(string path)
    {
        var media = await databaseService.GetMediaFolderFromPathAsync(path);

        if (media == null)
            return Json(null);

        return media.GetDTO();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<MediaStorageFolderDTO?>> GetById([Required] long id)
    {
        var folder = await databaseService.GetMediaFolderAsync(id);

        if (folder == null)
            return Json(null);

        return folder.GetDTO();
    }

    [HttpPost("addMediaToFolder")]
    public async Task<IActionResult> AddMediaToFolder([Required] string folderPath,
        [Required] long mediaConfigurationId, bool canCreateRootFolder = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return BadRequest("Path is missing");

        await databaseService.AddMediaToFolder(mediaConfigurationId, folderPath, canCreateRootFolder);

        return Ok();
    }

    [HttpPost("removeMediaFromFolder")]
    public async Task<IActionResult> RemoveMediaFromFolder([Required] string folderPath,
        [Required] long mediaConfigurationId)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return BadRequest("Path is missing");

        await databaseService.RemoveMediaFromFolder(mediaConfigurationId, folderPath);

        return Ok();
    }
}
