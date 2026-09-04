using Backend.Models;
using ImageMagick;

namespace Backend.Services;

public interface IMediaProcessingService
{
    public Task<string> ThumbnailPathForMedia(MediaFile media, string baseStorageLocation);

    /// <summary>
    ///   Returns a path where a modified version of the media is stored at.
    /// </summary>
    /// <param name="media">Media file to use. Must have cropping properties set!</param>
    /// <param name="baseStorageLocation">Base storage location</param>
    /// <returns>Path to read data from</returns>
    public Task<string> ModifiedMediaPath(MediaFile media, string baseStorageLocation);

    /// <summary>
    ///   Needs to be called when a media file has changed, so that the thumbnail can be regenerated.
    /// </summary>
    /// <param name="media">Media that changed</param>
    /// <param name="baseStorageLocation">Same base path as given to <see cref="ThumbnailPathForMedia"/></param>
    public void NotifyMediaFileChanged(MediaFile media, string baseStorageLocation);

    /// <summary>
    ///   This can be used to preview a media configuration that is not saved (the main MediaFile must be saved
    ///   already). This loads the data and processes it and then writes it out like a full file into the stream.
    /// </summary>
    /// <param name="media">Media file to use</param>
    /// <param name="baseStorageLocation">Where the original data is in</param>
    /// <returns>A full file stream with the processed content</returns>
    public Task<Stream> GetProcessedMediaStream(MediaFile media, string baseStorageLocation);

    /// <summary>
    ///   Applies media image settings to an image type media.
    /// </summary>
    /// <param name="mediaSettings">Settings</param>
    /// <param name="image">Target image</param>
    public void ApplyMediaImageAdjustments(MediaFile mediaSettings, MagickImageCollection image);

    /// <summary>
    ///   Creates a smaller thumbnail size video of a video.
    /// </summary>
    /// <param name="sourcePath">Source</param>
    /// <param name="destinationPath">Destination</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Task to wait</returns>
    public Task ResizeVideoThumbnailAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken);
}
