using System;
using System.IO;
using System.Threading.Tasks;
using DualView.Shared.Models.Enums;
using Backend.Services;
using ImageMagick;
using MediaType = DualView.Shared.Models.Enums.MediaType;

namespace DualView.GUI.Models;

public class LocalMediaSource : BaseMediaSource, IVisualMediaSource
{
    private readonly string path;

    public LocalMediaSource(string path, IServiceProvider videoPlayerServiceProvider) : base(videoPlayerServiceProvider)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("File not found", path);

        this.path = path;
    }

    public string LocalPath => path;

    public MediaType MediaType => MediaTypeExtensions.TypeFromExtension(Path.GetExtension(path));

    public async Task RequestLoadFull()
    {
        await LoadActionLock.WaitAsync();

        try
        {
            if (LoadStatus == IVisualMediaSource.LoadType.FullSize)
                return;

            PrepareForNewMedia();

            if (MediaType.IsImage())
            {
                if (MediaType.IsAnimated())
                {
                    var collection = new MagickImageCollection();
                    await collection.ReadAsync(path);

                    foreach (var frame in collection)
                    {
                        // Apply orientation if the image is rotated by metadata for consistent display
                        frame.AutoOrient();
                    }

                    LoadImage(null, collection);
                }
                else
                {
                    var image = new MagickImage();
                    await image.ReadAsync(path);
                    image.AutoOrient();
                    LoadImage(image, null);
                }
            }
            else
            {
                await StartVideo(File.OpenRead(path));
            }

            LoadStatus = IVisualMediaSource.LoadType.FullSize;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    public async Task RequestLoadThumbnail()
    {
        await LoadActionLock.WaitAsync();

        try
        {
            if (LoadStatus == IVisualMediaSource.LoadType.Thumbnail)
                return;

            PrepareForNewMedia();

            if (MediaType.IsImage())
            {
                if (MediaType.IsAnimated())
                {
                    var collection = new MagickImageCollection();
                    await collection.ReadAsync(path);

                    foreach (var frame in collection)
                    {
                        frame.AutoOrient();
                    }

                    // When animated, we would need to coalesce to get a smaller size for the thumbnail, so we kind of
                    // can't resize

                    LoadImage(null, collection);
                }
                else
                {
                    var image = new MagickImage();
                    await image.ReadAsync(path);

                    image.AutoOrient();

                    // Make a smaller size for the thumbnail
                    MediaProcessingService.ResizeWithDivisibleByTwoDimensions(image);

                    LoadImage(image, null);
                }
            }
            else
            {
                await StartVideo(File.OpenRead(path), true);
            }

            LoadStatus = IVisualMediaSource.LoadType.Thumbnail;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    public Task<string> GetName()
    {
        return Task.FromResult(Path.GetFileName(path));
    }

    public IVisualMediaSource Clone()
    {
        return new LocalMediaSource(path, VideoPlayerServiceProvider);
    }

    protected override void AdjustOutputResolution(ref int width, ref int height)
    {
        if (LoadStatus != IVisualMediaSource.LoadType.FullSize)
        {
            // We can't resize dynamically, but we can adjust the playback resolution.
            // Though setting this size does cause some error messages from VLC but seems to work (without this
            // we would need to resize things with an extra manual step to the target thumbnail size on frame read)
            (width, height) = MediaProcessingService.GetDivisibleByTwoDimensions(width, height);
        }
        else
        {
            base.AdjustOutputResolution(ref width, ref height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
        }
    }
}
