using Backend.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/scannedCollection")]
public sealed class ScannedCollectionController : Controller
{
    private readonly IDatabaseService databaseService;
    private readonly IRemoteGalleryScannerService scannerService;
    private readonly IRemoteGalleryMediaService mediaService;

    public ScannedCollectionController(IDatabaseService databaseService,
        IRemoteGalleryScannerService scannerService, IRemoteGalleryMediaService mediaService)
    {
        this.databaseService = databaseService;
        this.scannerService = scannerService;
        this.mediaService = mediaService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ScannedCollectionDTO>>> GetAll(CancellationToken cancellationToken)
    {
        var collections = await databaseService.GetScannedCollectionsAsync(cancellationToken);
        return collections.Select(collection => collection.GetDTO()).ToList();
    }

    [HttpGet("{id:long}/thumbnail/{mediaId:long}")]
    public async Task<ActionResult> GetThumbnail(long id, long mediaId, CancellationToken cancellationToken)
    {
        var collection = await databaseService.GetScannedCollectionAsync(id, cancellationToken);
        if (collection == null || collection.FoundMedia.All(media => media.Id != mediaId))
            return NotFound();
        var path = await mediaService.GetThumbnailAsync(mediaId, cancellationToken);
        var mediaType = MediaTypeExtensions.TypeFromExtension(Path.GetExtension(path));
        return PhysicalFile(path, mediaType.ToMimeType());
    }

    [HttpGet("{id:long}/content/{mediaId:long}")]
    public async Task<ActionResult> GetContent(long id, long mediaId, CancellationToken cancellationToken)
    {
        var collection = await databaseService.GetScannedCollectionAsync(id, cancellationToken);
        if (collection == null || collection.FoundMedia.All(media => media.Id != mediaId))
            return NotFound();
        var path = await mediaService.GetFullMediaAsync(mediaId, cancellationToken);
        var mediaType = MediaTypeExtensions.TypeFromExtension(Path.GetExtension(path));
        return PhysicalFile(path, mediaType.ToMimeType());
    }

    [HttpPost("{id:long}/pause")]
    public async Task<IActionResult> Pause(long id, CancellationToken cancellationToken)
    {
        await scannerService.PauseGalleryAsync(id, cancellationToken);
        return Ok();
    }

    [HttpPost("media/{mediaId:long}/selection")]
    public async Task<IActionResult> SetSelection(long mediaId, [FromBody] bool selected,
        CancellationToken cancellationToken)
    {
        var media = await databaseService.GetFoundMediaAsync(mediaId);
        if (media == null)
            return NotFound();
        media.Selected = selected;
        await databaseService.SaveFoundMediaAsync(media);
        return Ok();
    }

    [HttpPost("{id:long}/resume")]
    public async Task<IActionResult> Resume(long id, CancellationToken cancellationToken)
    {
        await scannerService.ResumeGalleryAsync(id, cancellationToken);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await scannerService.DeleteGalleryAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:long}/accept")]
    public async Task<IActionResult> Accept(long id, AcceptScannedCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await scannerService.AcceptGalleryAsync(id, request.Name, request.MediaIds, cancellationToken);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
