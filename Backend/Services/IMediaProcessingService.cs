using Backend.Models;
using ImageMagick;

namespace Backend.Services;

public interface IMediaProcessingService
{
    public Task<string> ThumbnailPathForMedia(ConfiguredMedia media, string baseStorageLocation);

    /// <summary>
    ///   Returns a path where a modified version of the media is stored at.
    /// </summary>
    /// <param name="media">Media config to use. Must not be prime!</param>
    /// <param name="originalMedia">
    ///   If the media doesn't have the navigation loaded, this needs to specify the original media.
    /// </param>
    /// <param name="baseStorageLocation">Base storage location</param>
    /// <returns>Path to read data from</returns>
    public Task<string> ModifiedMediaPath(ConfiguredMedia media, MediaFile? originalMedia, string baseStorageLocation);

    /// <summary>
    ///   Needs to be called when a media config has changed, so that the thumbnail can be regenerated.
    ///   Note only valid for non-prime media, and the config must have the main media navigation loaded.
    /// </summary>
    /// <param name="media">Media that changed</param>
    /// <param name="baseStorageLocation">Same base path as given to <see cref="ThumbnailPathForMedia"/></param>
    public void NotifyMediaConfigChanged(ConfiguredMedia media, string baseStorageLocation);

    /// <summary>
    ///   This can be used to preview a media configuration that is not saved (the main MediaFile must be saved
    ///   already). This loads the data and processes it and then writes it out like a full file into the stream.
    /// </summary>
    /// <param name="media">Media config to use</param>
    /// <param name="baseStorageLocation">Where the original data is in</param>
    /// <returns>A full file stream with the processed content</returns>
    public Task<Stream> GetProcessedMediaStream(ConfiguredMedia media, string baseStorageLocation);

    /// <summary>
    ///   Applies media image settings to an image type media.
    /// </summary>
    /// <param name="mediaSettings">Settings</param>
    /// <param name="image">Target image</param>
    public void ApplyMediaImageAdjustments(ConfiguredMedia mediaSettings, MagickImageCollection image);
}
