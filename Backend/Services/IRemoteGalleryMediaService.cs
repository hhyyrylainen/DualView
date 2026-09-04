namespace Backend.Services;

/// <summary>
///   Retrieves cached remote-gallery previews and original content. Returns file paths.
/// </summary>
public interface IRemoteGalleryMediaService
{
    public Task<string> GetThumbnailAsync(long itemId, CancellationToken cancellationToken);
    public Task<string> GetFullMediaAsync(long itemId, CancellationToken cancellationToken);
}
