using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Requests;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MediaController : Controller
{
    private static readonly SemaphoreSlim PreviewProcessingLock = new(2, 2);

    private readonly ILogger<MediaController> logger;
    private readonly IDatabaseService databaseService;
    private readonly IMediaImportHandler mediaImportHandler;
    private readonly IMediaProcessingService mediaProcessingService;
    private readonly IDataFolderService dataFolderService;

    public MediaController(ILogger<MediaController> logger, IDatabaseService databaseService,
        IMediaImportHandler mediaImportHandler, IMediaProcessingService mediaProcessingService,
        IDataFolderService dataFolderService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.mediaImportHandler = mediaImportHandler;
        this.mediaProcessingService = mediaProcessingService;
        this.dataFolderService = dataFolderService;
    }

    [HttpGet("{mediaConfigId:long}")]
    public async Task<ActionResult<ConfiguredMediaDTO>> GetFullMedia([Required] long mediaConfigId,
        bool loadMedia = true)
    {
        var media = await databaseService.GetConfiguredMediaAsync(mediaConfigId, loadMedia);

        if (media == null)
            return NotFound();

        return media.GetDTO();
    }

    [HttpGet("byMediaFileId/{mediaFileId:long}")]
    public async Task<ActionResult<ConfiguredMediaDTO>> GetByMediaId([Required] long mediaFileId,
        bool loadMedia = true)
    {
        var media = await databaseService.GetConfiguredMediaByMediaFileAsync(mediaFileId, loadMedia);

        if (media == null)
            return Json(null);

        return media.GetDTO();
    }

    [HttpPost("import")]
    public async Task<ActionResult<ConfiguredMediaDTO>> ImportMedia([FromQuery] [Required] string targetFolder,
        [Required] IFormFile file)
    {
        if (file.Length == 0)
            return BadRequest("No file was uploaded.");

        if (string.IsNullOrEmpty(file.FileName) || string.IsNullOrEmpty(Path.GetExtension(file.FileName)))
            return BadRequest("File name is invalid");

        // Access the stream from the uploaded file
        await using var stream = file.OpenReadStream();

        // We want to keep all user-imported media permanently without auto delete
        var primeMediaConfiguration = await mediaImportHandler.ImportMedia(file.FileName, stream, targetFolder, true,
            null);

        return primeMediaConfiguration.GetDTO();
    }

    [HttpGet("{mediaConfigId:long}/inFolders")]
    public async Task<ActionResult<MediaConfigFolderInfo>> GetFoldersMediaConfigIsIn([Required] long mediaConfigId)
    {
        var media = await databaseService.GetConfiguredMediaAsync(mediaConfigId);

        if (media == null)
            return NotFound();

        return await databaseService.GetConfiguredMediaFoldersAsync(media.Id);
    }

    [HttpGet("{mediaConfigId:long}/siblings")]
    public async Task<ActionResult<List<ConfiguredMediaDTO>>> GetSiblings([Required] long mediaConfigId)
    {
        var media = await databaseService.GetConfiguredMediaAsync(mediaConfigId);

        if (media == null)
            return NotFound();

        var allConfigs = await databaseService.GetMediaConfigurationsAsync(media.MediaFileId);

        return allConfigs.Where(c => c.Id != mediaConfigId).Select(c => c.GetDTO()).ToList();
    }

    [HttpPost("createConfig")]
    public async Task<ActionResult<ConfiguredMediaDTO>> CreateNewMediaConfig(
        [FromBody] CreateMediaConfigRequest request)
    {
        var originalMedia = await databaseService.GetMediaByIdAsync(request.ParentMediaId);

        if (originalMedia == null)
            return NotFound();

        var mainFolder = request.FoldersToAdd?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(mainFolder))
            return BadRequest("Main folder cannot be empty");

        // Check if the name conflicts
        var conflict = await databaseService.GetMediaByNameAndPath(request.Name, mainFolder);

        if (conflict != null)
            return BadRequest("A media with the same name already exists in the folder");

        logger.LogInformation("Creating new config for media: {Id}, in folder: {Folder}", originalMedia.Id, mainFolder);
        var newConfig = await databaseService.CreateMediaConfig(originalMedia, request.Name, mainFolder,
            request.MarkAsKeep, request.CreateFolders, request.CreateFolders);

        if (request.FoldersToAdd != null)
        {
            // Add the extra folders
            for (int i = 1; i < request.FoldersToAdd.Count; ++i)
            {
                var folder = request.FoldersToAdd[i];
                try
                {
                    await databaseService.AddMediaToFolder(newConfig.Id, folder, request.CreateFolders);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to add media to folder {Folder}, but will continue creation", folder);
                    continue;
                }

                logger.LogInformation("Added new media config {Id} to folder: {Folder}", newConfig.Id, folder);
            }
        }

        return newConfig.GetDTO();
    }

    [HttpPut("{configId:long}")]
    public async Task<ActionResult> UpdateConfig(long configId, [FromBody] ConfiguredMediaDTO request)
    {
        if (configId != request.Id)
            return BadRequest("ID mismatch");

        var media = await databaseService.GetConfiguredMediaAsync(configId, true);

        if (media == null || media.IsDeleted)
            return NotFound();

        if (media.Prime)
            return BadRequest("Cannot edit prime media");

        if (request.Name != media.Name)
        {
            // Check if the name conflicts
            var folders = await databaseService.GetConfiguredMediaFoldersAsync(configId);

            var conflict = await databaseService.GetMediaByNameAndPath(request.Name, folders.PrimaryFolder);

            if (conflict != null)
                return BadRequest("A media with the same name already exists in the folder");

            if (folders.SecondaryFolders != null)
            {
                foreach (var folder in folders.SecondaryFolders)
                {
                    conflict = await databaseService.GetMediaByNameAndPath(request.Name, folder);

                    if (conflict != null)
                        return BadRequest("A media with the same name already exists in a secondary folder");
                }
            }
        }

        if (!media.UpdateFromClient(request))
        {
            return Ok("No changes were made");
        }

        media.BumpUpdatedAtTime();
        media.RefreshDerivedStatistics();

        logger.LogInformation("Updating media config {Id} (for media: {Id2})", configId, media.MediaFileId);

        await databaseService.SaveMediaConfigAsync(media);

        try
        {
            var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);
            mediaProcessingService.NotifyMediaConfigChanged(media, storage);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to update thumbnail / saved file after edit");
        }

        // TODO: do we want to notify *all* folders this is in of the change?

        return Ok();
    }

    [HttpPost("{configId:long}/keepStatus")]
    public async Task<ActionResult> SetKeepStatus([Required] long configId, [Required] bool keep)
    {
        try
        {
            if (await databaseService.SetMediaKeepStatusAsync(configId, keep))
            {
                logger.LogInformation("Set keep status for media config {Id} to {Keep}", configId, keep);
                return Created();
            }

            return Ok();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpGet("{configId:long}/safeToDelete")]
    public async Task<ActionResult<bool>> IsSafeToDelete([Required] long configId)
    {
        return await databaseService.IsMediaSafeToDeleteAsync(configId);
    }

    [HttpDelete("{configId:long}")]
    public async Task<ActionResult> DeleteMedia([Required] long configId)
    {
        var media = await databaseService.GetConfiguredMediaAsync(configId);

        if (media == null)
            return NotFound();

        if (media.IsDeleted)
            return Ok("Already deleted");

        await databaseService.DeleteMediaAsync(configId);

        logger.LogInformation("Deleted media config {Id}", configId);

        return Ok();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<List<ConfiguredMediaDTO>>> GetDeletedMedia([FromQuery] int limit = 100)
    {
        var deleted = await databaseService.GetDeletedMediaAsync(limit);
        return deleted.Select(m => m.GetDTO()).ToList();
    }

    [HttpPost("{configId:long}/restore")]
    public async Task<ActionResult> RestoreMedia([Required] long configId)
    {
        try
        {
            await databaseService.RestoreMediaAsync(configId);
            return Ok();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpPost("{configId:long}/previewChanges")]
    public async Task<ActionResult> PreviewConfigParameters(long configId, [FromBody] ConfiguredMediaDTO request)
    {
        if (configId != request.Id)
            return BadRequest("ID mismatch");

        var media = await databaseService.GetConfiguredMediaAsync(configId, true);

        if (media == null || media.IsDeleted)
            return NotFound();

        // We allow previewing changes on prime media
        media.Prime = false;
        request.Prime = false;

        Stream stream;

        var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);

        // Prevent a ton of processing happening at once that could eat up all performance
        await PreviewProcessingLock.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (!media.UpdateFromClient(request))
            {
                // No changes actually, so we could return an old cached copy *if* one is made
                return File(
                    System.IO.File.OpenRead(await mediaProcessingService.ModifiedMediaPath(media, null, storage)),
                    media.MediaType.ToMimeType(), false);
            }

            media.RefreshDerivedStatistics();

            stream = await mediaProcessingService.GetProcessedMediaStream(media, storage);
        }
        finally
        {
            PreviewProcessingLock.Release();
        }

        return File(stream, media.MediaType.ToMimeType(), false);
    }
}
