using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Requests;
using Backend.Models;
using Backend.Services;
using Backend.Utilities;
using ImageMagick;
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

    /// <summary>
    ///   Detects an image's actual format and updates its stored type and file extension when needed.
    /// </summary>
    [HttpPost("{mediaId:long}/redetectType")]
    public async Task<ActionResult<MediaFileDTO>> RedetectType([Required] long mediaId,
        CancellationToken cancellationToken)
    {
        var media = await databaseService.GetMediaByIdAsync(mediaId);

        if (media == null || media.IsDeleted)
            return NotFound();

        if (!media.MediaType.IsImage())
            return BadRequest("Only image media types can be redetected.");

        cancellationToken.ThrowIfCancellationRequested();

        var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);
        var originalPath = Path.GetFullPath(Path.Combine(storage, media.PathRelativeToStorage()));
        if (!System.IO.File.Exists(originalPath))
            return NotFound("The media file is missing from storage.");

        MagickFormat detectedFormat;
        int frameCount;

        try
        {
            await using var imageStream = System.IO.File.OpenRead(originalPath);
            using var images = new MagickImageCollection();
            await images.ReadAsync(imageStream, cancellationToken);
            if (images.Count == 0)
                return UnprocessableEntity("ImageMagick did not find any image frames.");

            detectedFormat = images[0].Format;
            frameCount = images.Count;
        }
        catch (MagickException exception)
        {
            logger.LogWarning(exception, "ImageMagick could not detect the format for media {MediaId}", mediaId);
            return UnprocessableEntity("ImageMagick could not read this image.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var detectedExtension = FileProbe.GetExtensionForFormat(detectedFormat);
        if (detectedExtension == null)
            return UnprocessableEntity("The detected image format is not supported.");

        var detectedMediaType = MediaTypeExtensions.TypeFromExtension(detectedExtension);
        if (detectedMediaType == MediaType.Webp && frameCount > 1)
            detectedMediaType = MediaType.WebpAnimated;

        var originalFileName = media.OriginalFileName;
        var originalMediaType = media.MediaType;
        var correctedFileName = Path.ChangeExtension(originalFileName, detectedExtension);
        var newMediaPath = originalPath;

        if (!string.Equals(originalFileName, correctedFileName, StringComparison.Ordinal))
        {
            try
            {
                media.OriginalFileName = correctedFileName;
                newMediaPath = Path.GetFullPath(Path.Combine(storage, media.PathRelativeToStorage()));
            }
            finally
            {
                media.OriginalFileName = originalFileName;
            }
        }

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var shouldMoveFile = !string.Equals(originalPath, newMediaPath, pathComparison);

        if (!string.Equals(originalFileName, correctedFileName, StringComparison.Ordinal) ||
            originalMediaType != detectedMediaType)
        {
            if (shouldMoveFile && System.IO.File.Exists(newMediaPath))
                return Conflict("A file with the detected extension already exists in storage.");

            mediaProcessingService.NotifyMediaFileChanged(media, storage);

            if (shouldMoveFile)
                System.IO.File.Move(originalPath, newMediaPath);

            media.OriginalFileName = correctedFileName;
            media.MediaType = detectedMediaType;

            try
            {
                await databaseService.SaveMediaFileAsync(media);
            }
            catch (Exception exception)
            {
                await databaseService.ReloadEntity(media);
                if (media.OriginalFileName == originalFileName && media.MediaType == originalMediaType)
                {
                    if (shouldMoveFile && System.IO.File.Exists(newMediaPath) &&
                        !System.IO.File.Exists(originalPath))
                    {
                        System.IO.File.Move(newMediaPath, originalPath);
                    }

                    throw;
                }

                logger.LogWarning(exception,
                    "Media {MediaId} was updated, but saving its update failed", mediaId);
            }

            mediaProcessingService.NotifyMediaFileChanged(media, storage);
        }

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

        var extension = Path.GetExtension(file.FileName);
        var normalizedFileName = file.FileName[..^extension.Length] + extension.ToLowerInvariant();

        // Access the stream from the uploaded file
        await using var stream = file.OpenReadStream();

        var media = await mediaImportHandler.ImportMedia(normalizedFileName, stream, sectionName, sourcePath);

        return media.GetDTO();
    }

    [HttpPost("appliedTag/bulk")]
    public async Task<IActionResult> AddAppliedTagsToMedia([FromBody] AddParsedAppliedTagsToMediaRequest request)
    {
        if (request.MediaIds.Count == 0 || request.AppliedTags.Count == 0)
            return BadRequest("Media IDs and applied tags cannot be empty");

        await databaseService.AddParsedAppliedTagsToMediaAsync(request.MediaIds, request.AppliedTags);
        return Ok();
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
    public async Task<ActionResult<long>> AddAppliedTag([Required] long mediaId,
        [FromBody] AddAppliedTagRequest request)
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
