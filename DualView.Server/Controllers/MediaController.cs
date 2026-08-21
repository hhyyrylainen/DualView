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

    [HttpGet("{mediaId:long}")]
    public async Task<ActionResult<MediaFileDTO>> GetFullMedia([Required] long mediaId, bool isView = true)
    {
        var media = isView
            ? await databaseService.GetMediaByIdAsync(mediaId)
            : await databaseService.GetMediaByIdIncludingDeletedAsync(mediaId);

        if (media == null)
            return NotFound();

        return media.GetDTO();
    }

    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<MediaFileDTO>> ImportMedia([FromQuery] string? sectionName,
        [FromQuery] string? sourcePath, [Required] IFormFile file)
    {
        if (file.Length == 0)
            return BadRequest("No file was uploaded.");

        if (string.IsNullOrEmpty(file.FileName) || string.IsNullOrEmpty(Path.GetExtension(file.FileName)))
            return BadRequest("File name is invalid");

        // Access the stream from the uploaded file
        await using var stream = file.OpenReadStream();

        var media = await mediaImportHandler.ImportMedia(file.FileName, stream, sectionName, sourcePath);

        return media.GetDTO();
    }

    [HttpGet("{mediaId:long}/collections")]
    public async Task<ActionResult<List<long>>> GetCollectionsMediaIsIn([Required] long mediaId)
    {
        return await databaseService.GetMediaCollectionsAsync(mediaId);
    }

    [HttpGet("{mediaId:long}/siblings")]
    public async Task<ActionResult<List<MediaFileDTO>>> GetSiblings([Required] long mediaId)
    {
        var siblings = await databaseService.GetMediaFileSiblingsAsync(mediaId);
        return siblings.Select(m => m.GetDTO()).ToList();
    }

    [HttpPut("{mediaId:long}")]
    public async Task<ActionResult> UpdateMedia(long mediaId, [FromBody] MediaFileDTO request)
    {
        if (mediaId != request.Id)
            return BadRequest("ID mismatch");

        var media = await databaseService.GetMediaByIdAsync(mediaId);

        if (media == null || media.IsDeleted)
            return NotFound();

        // Update properties
        media.CropLeft = request.CropLeft;
        media.CropTop = request.CropTop;
        media.CropRight = request.CropRight;
        media.CropBottom = request.CropBottom;

        await databaseService.SaveMediaFileAsync(media);

        try
        {
            var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);
            mediaProcessingService.NotifyMediaFileChanged(media, storage);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to update thumbnail / saved file after edit");
        }

        return Ok();
    }

    [HttpGet("{mediaId:long}/safeToDelete")]
    public async Task<ActionResult<bool>> IsSafeToDelete([Required] long mediaId)
    {
        return await databaseService.IsMediaSafeToDeleteAsync(mediaId);
    }

    [HttpDelete("{mediaId:long}")]
    public async Task<ActionResult> DeleteMedia([Required] long mediaId)
    {
        await databaseService.DeleteMediaAsync(mediaId);

        logger.LogInformation("Deleted media {Id}", mediaId);

        return Ok();
    }

    [HttpPost("{mediaId:long}/temporaryStatus")]
    public async Task<ActionResult> SetTemporaryStatus([Required] long mediaId, [Required] bool isTemporary)
    {
        await databaseService.SetMediaTemporaryStatusAsync(mediaId, isTemporary);
        return Ok();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<List<MediaFileDTO>>> GetDeletedMedia([FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        return (await databaseService.GetDeletedMediaAsync(limit, offset)).ConvertToDTO<MediaFile, MediaFileDTO>();
    }

    [HttpPost("{mediaId:long}/restore")]
    public async Task<ActionResult> RestoreMedia([Required] long mediaId)
    {
        try
        {
            await databaseService.RestoreMediaAsync(mediaId);
            return Ok();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpPost("{mediaId:long}/previewChanges")]
    public async Task<ActionResult> PreviewChanges(long mediaId, [FromBody] MediaFileDTO request)
    {
        if (mediaId != request.Id)
            return BadRequest("ID mismatch");

        var media = await databaseService.GetMediaByIdAsync(mediaId);

        if (media == null || media.IsDeleted)
            return NotFound();

        // Update properties on the local object for preview
        media.CropLeft = request.CropLeft;
        media.CropTop = request.CropTop;
        media.CropRight = request.CropRight;
        media.CropBottom = request.CropBottom;

        Stream stream;

        var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);

        // Prevent a ton of processing happening at once that could eat up all performance
        await PreviewProcessingLock.WaitAsync(HttpContext.RequestAborted);
        try
        {
            stream = await mediaProcessingService.GetProcessedMediaStream(media, storage);
        }
        finally
        {
            PreviewProcessingLock.Release();
        }

        return File(stream, media.MediaType.ToMimeType(), false);
    }

    [HttpPost("{mediaId:long}/appliedTag")]
    public async Task<ActionResult<long>> AddAppliedTag([Required] long mediaId, [FromBody] AddAppliedTagRequest request)
    {
        if (request.ParsedTag != null)
            return Ok(await databaseService.AddParsedAppliedTagToMediaAsync(mediaId, request.ParsedTag));

        var id = await databaseService.AddAppliedTagToMediaAsync(mediaId, request.TagId, request.ModifierIds,
            request.CombinedWithAppliedTagId, request.CombineWord);
        return Ok(id);
    }

    [HttpDelete("{mediaId:long}/appliedTag/{appliedTagId:long}")]
    public async Task<IActionResult> RemoveAppliedTag([Required] long mediaId, [Required] long appliedTagId)
    {
        await databaseService.RemoveAppliedTagFromMediaAsync(mediaId, appliedTagId);
        return Ok();
    }

    [HttpGet("{mediaId:long}/appliedTag")]
    public async Task<ActionResult<List<AppliedTagDTO>>> GetAppliedTags([Required] long mediaId)
    {
        return (await databaseService.GetMediaAppliedTagsAsync(mediaId)).ConvertToDTO<AppliedTag, AppliedTagDTO>();
    }
}
