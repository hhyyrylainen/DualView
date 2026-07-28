using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.Enums;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MediaContentController : Controller
{
    /// <summary>
    ///   Used to prevent an absolute ton of threads from trying to generate thumbnails at once.
    /// </summary>
    private static readonly SemaphoreSlim ThumbnailProcessingLock = new(12, 12);

    private readonly ILogger<MediaContentController> logger;
    private readonly IDatabaseService databaseService;
    private readonly IDataFolderService dataFolderService;
    private readonly IMediaProcessingService mediaProcessingService;

    public MediaContentController(ILogger<MediaContentController> logger, IDatabaseService databaseService,
        IDataFolderService dataFolderService, IMediaProcessingService mediaProcessingService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.dataFolderService = dataFolderService;
        this.mediaProcessingService = mediaProcessingService;
    }

    [HttpGet("{mediaConfigId:long}/content")]
    public async Task<ActionResult> GetFullMediaContent([Required] long mediaConfigId)
    {
        var media = await databaseService.GetConfiguredMediaAsync(mediaConfigId);

        if (media == null)
            return NotFound();

        var localPath =
            await MediaImportHandler.GetMediaLocalPath(media, databaseService, dataFolderService,
                mediaProcessingService);

        if (localPath == null)
            return NotFound();

        // This gives the shortened name, but as it is basically never downloaded, this should be fine;
        // we can add a separate endpoint for browser downloads if wanted
        return File(System.IO.File.OpenRead(localPath), media.MediaType.ToMimeType(),
            media.Name, true);
    }

    [HttpGet("{mediaConfigId:long}/thumbnail")]
    public async Task<ActionResult> GetThumbnail([Required] long mediaConfigId)
    {
        var media = await databaseService.GetConfiguredMediaAsync(mediaConfigId, true);

        if (media == null)
            return NotFound();

        string path;

        if (!await ThumbnailProcessingLock.WaitAsync(TimeSpan.FromSeconds(60), HttpContext.RequestAborted))
        {
            logger.LogWarning(
                "Thumbnail generation is being overloaded, will proceed trying to use more CPU after wait timed out");
            path = await GetThumbnailPathInternal(media);
        }
        else
        {
            try
            {
                path = await GetThumbnailPathInternal(media);
            }
            finally
            {
                ThumbnailProcessingLock.Release();
            }
        }

        return File(System.IO.File.OpenRead(path), media.MediaType.ToMimeType(),
            "thumb_" + media.MediaFile.OriginalFileName);
    }

    [NonAction]
    private async Task<string> GetThumbnailPathInternal(ConfiguredMedia media)
    {
        var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);
        return await mediaProcessingService.ThumbnailPathForMedia(media, storage);
    }
}
