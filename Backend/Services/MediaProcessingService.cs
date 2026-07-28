using System.Diagnostics;
using System.Globalization;
using DualView.Shared.Models.Enums;
using Backend.Models;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class MediaProcessingService : IMediaProcessingService
{
    public const int ThumbnailSize = 256;
    public const int AnimatedThumbnailSize = 196;

    public const int VideoThumbnailSize = 256;

    // Tradeoff between size and creation time (vp9 is about 10x slower, but a third smaller size)
    // public const string VideoThumbnailCodec = "libvpx-vp9";
    public const string VideoThumbnailCodec = "libvpx";

    /// <summary>
    ///   Cuts the preview video length as a preview doesn't need to be super long
    /// </summary>
    public const float MaxThumbVideoLength = 10;

    /// <summary>
    ///   Reduce the frame rate to save on bandwidth
    /// </summary>
    public const int ThumbnailVideoFrameRate = 16;

    public const int KeepAnimatedThumbnailNthFrame = 2;

    private static readonly SemaphoreSlim ThumbnailProcessingLock = new(4, 4);

    private static readonly List<long> ActiveMediaProcessingIds = new();

    private readonly ILogger<MediaProcessingService> logger;

    public MediaProcessingService(ILogger<MediaProcessingService> logger)
    {
        this.logger = logger;
    }

    public static void ResizeWithDivisibleByTwoDimensions(IMagickImage image, int thumbnailSize = ThumbnailSize)
    {
        var (newWidth, newHeight) = GetDivisibleByTwoDimensions(image.Width, image.Height, thumbnailSize);

        image.Resize(newWidth, newHeight, FilterType.Lanczos);
    }

    public static (int Width, int Height) GetDivisibleByTwoDimensions(int width, int height,
        int thumbnailSize = ThumbnailSize)
    {
        var (newWidth, newHeight) = GetDivisibleByTwoDimensions((uint)width, (uint)height, thumbnailSize);

        return ((int)newWidth, (int)newHeight);
    }

    public static (uint Width, uint Height) GetDivisibleByTwoDimensions(uint width, uint height,
        int thumbnailSize = ThumbnailSize)
    {
        // Calculate proportional dimensions that are divisible by 2
        double ratio = (double)width / height;
        int newWidth, newHeight;

        if (width > height)
        {
            newWidth = thumbnailSize;
            newHeight = (int)Math.Round(thumbnailSize / ratio);
        }
        else
        {
            newHeight = thumbnailSize;
            newWidth = (int)Math.Round(thumbnailSize * ratio);
        }

        // Ensure both dimensions are even
        newWidth = (newWidth >> 1) << 1;
        newHeight = (newHeight >> 1) << 1;

        // Ensure we don't end up with 0
        newWidth = Math.Max(2, newWidth);
        newHeight = Math.Max(2, newHeight);

        return ((uint)newWidth, (uint)newHeight);
    }

    public async Task<string> ThumbnailPathForMedia(ConfiguredMedia media, string baseStorageLocation)
    {
        // The navigation must be loaded
        var original = media.MediaFile ?? throw new InvalidOperationException("MediaFile navigation must be loaded");

        // Gate starting processing for the same thing at the same time
        await WaitForExistingMediaProcessing(media);

        try
        {
            if (!media.MediaType.IsImage())
            {
                // Video handling
                return await SmallVideoPath(media, baseStorageLocation, original);
            }

            return await ThumbnailPath(media, baseStorageLocation, original);
        }
        finally
        {
            ReportMediaProcessingEnd(media);
        }
    }

    public async Task<string> ModifiedMediaPath(ConfiguredMedia media, MediaFile? originalMedia,
        string baseStorageLocation)
    {
        if (media.Prime)
            throw new ArgumentException("Prime media can never be modified, use a different API to access that");

        var original = media.MediaFile;

        if (original == null!)
            original = originalMedia ?? throw new InvalidOperationException("MediaFile navigation must be loaded");

        media.MediaFile = original;

        // Theoretically we don't need to wait for thumbnails but for simplicity we just do
        await WaitForExistingMediaProcessing(media);

        try
        {
            var path = GetPathForProcessedMedia(media, baseStorageLocation);

            if (Path.Exists(path))
                return path;

            if (!media.MediaType.IsImage())
            {
                // Video handling
                throw new NotImplementedException("Video previewing changes is not implemented");
            }

            using var image = new MagickImageCollection();

            var originalPath = Path.Join(baseStorageLocation, original.PathRelativeToStorage());
            await image.ReadAsync(originalPath).ConfigureAwait(false);

            if (image.Count < 1)
                throw new InvalidOperationException("No frames found in image for applying changes");

            image.Coalesce();

            // Auto-orient images if rotated by metadata for consistency before processing
            foreach (var frame in image)
            {
                frame.AutoOrient();
            }

            ApplyMediaImageAdjustments(media, image);

            var folder = Path.GetDirectoryName(path) ?? throw new Exception("Failed to detect target folder");
            Directory.CreateDirectory(folder);

            await image.WriteAsync(path).ConfigureAwait(false);
            return path;
        }
        finally
        {
            ReportMediaProcessingEnd(media);
        }
    }

    public void NotifyMediaConfigChanged(ConfiguredMedia media, string baseStorageLocation)
    {
        if (media.MediaFile == null)
            throw new InvalidOperationException("MediaFile navigation must be loaded");

        // TODO: should we lock here to make sure no jobs will generate the files again immediately?
        var path = GetPathForProcessedMedia(media, baseStorageLocation);
        var path2 = GetPathForProcessedMedia(media, baseStorageLocation, true);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path2))
            {
                File.Delete(path2);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete processed file for changed media");
        }

        var thumbnailPath = GetPathForThumbnail(media, baseStorageLocation);

        try
        {
            if (File.Exists(thumbnailPath))
                File.Delete(thumbnailPath);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete thumbnail file(s) for changed media");
        }
    }

    public string GetPathForProcessedMedia(ConfiguredMedia media, string baseStorageLocation, bool includeMask = false)
    {
        if (media.Prime)
            throw new ArgumentException("Prime config can never change so it cannot have a processed path");

        // The navigation must be loaded
        var original = media.MediaFile ?? throw new InvalidOperationException("MediaFile navigation must be loaded");

        var file =
            $"{media.Id}{(includeMask && media.MaskEnabled ? "_masked" : "")}{Path.GetExtension(original.OriginalFileName)}";

        return Path.Combine(baseStorageLocation, "processed", $"{media.Id % 100}", file);
    }

    public void ApplyMediaImageAdjustments(ConfiguredMedia mediaSettings, MagickImageCollection image)
    {
        if (mediaSettings.Prime)
            logger.LogWarning("Uselessly applying prime media config adjustments (there should be none)");

        // Apply each frame edit
        foreach (var singleFrame in image)
        {
            ApplyMediaImageAdjustments(mediaSettings, singleFrame, false);
        }

        if (mediaSettings.CropStart is > 0)
        {
            for (int i = 0; i < mediaSettings.CropStart.Value; ++i)
            {
                // Keep at least one frame
                if (image.Count <= 1)
                    break;

                image.RemoveAt(0);
            }
        }

        if (mediaSettings.CropEnd is > 0)
        {
            for (int i = 0; i < mediaSettings.CropEnd.Value; ++i)
            {
                // Keep at least one frame
                if (image.Count <= 1)
                    break;

                image.RemoveAt(image.Count - 1);
            }
        }
    }

    public void ApplyMediaImageAdjustments(ConfiguredMedia mediaSettings, IMagickImage singleFrame,
        bool warnOnPrime = true)
    {
        if (mediaSettings.Prime && warnOnPrime)
            logger.LogWarning("Uselessly applying prime media config adjustments (there should be none)");

        if (mediaSettings.FlipHorizontal)
            singleFrame.Flop();

        if (mediaSettings.FlipVertical)
            singleFrame.Flip();

        if (mediaSettings.CropLeft > 0 || mediaSettings.CropTop > 0 || mediaSettings.CropRight > 0 ||
            mediaSettings.CropBottom > 0)
        {
            singleFrame.Crop(new MagickGeometry(mediaSettings.CropLeft, mediaSettings.CropTop,
                singleFrame.Width - (uint)mediaSettings.CropRight,
                singleFrame.Height - (uint)mediaSettings.CropBottom));
        }

        if (Math.Abs(mediaSettings.Scale - 1) > 0.001f)
            singleFrame.Scale(new Percentage(mediaSettings.Scale * 100));

        if (mediaSettings.Rotation != 0)
            singleFrame.Rotate(mediaSettings.Rotation);
    }

    public async Task<Stream> GetProcessedMediaStream(ConfiguredMedia media, string baseStorageLocation)
    {
        var original = media.MediaFile ?? throw new InvalidOperationException("MediaFile navigation must be loaded");

        if (!media.MediaType.IsImage())
        {
            // Video handling
            throw new NotImplementedException("Video previewing changes is not implemented");
        }

        using var image = new MagickImageCollection();

        var originalPath = Path.Join(baseStorageLocation, original.PathRelativeToStorage());
        await image.ReadAsync(originalPath).ConfigureAwait(false);

        if (image.Count < 1)
            throw new InvalidOperationException("No frames found in image for applying changes");

        image.Coalesce();

        // Auto-orient images if rotated by metadata for consistency before processing
        foreach (var frame in image)
        {
            frame.AutoOrient();
        }

        if (media.Prime)
        {
            // Prime, so we don't need to do anything
        }
        else
        {
            ApplyMediaImageAdjustments(media, image);
        }

        var targetStream = new MemoryStream();
        await image.WriteAsync(targetStream).ConfigureAwait(false);
        targetStream.Position = 0;
        return targetStream;
    }

    private static string GetPathForThumbnail(ConfiguredMedia media, string baseStorageLocation)
    {
        string thumbnailExtension;

        if (media.MediaType.IsImage())
        {
            thumbnailExtension = Path.GetExtension(media.MediaFile.OriginalFileName);
        }
        else
        {
            thumbnailExtension = ".webm";
        }

        // By default, thumbnails are masked, so that is the main name here
        var thumbnailFile = $"{media.Id}_um{thumbnailExtension}";

        // To not pack all thumbnails in the same folder, we use the media ID as a prefix (with a modulo)
        return Path.Combine(baseStorageLocation, "thumbnails", $"{media.Id % 100}", thumbnailFile);
    }

    private async Task<string> ThumbnailPath(ConfiguredMedia media, string baseStorageLocation, MediaFile original)
    {
        var thumbnailPath = GetPathForThumbnail(media, baseStorageLocation);

        if (File.Exists(thumbnailPath))
        {
            // Exists already, return it
            return thumbnailPath;
        }

        // Need to generate a new thumbnail
        logger.LogInformation("Generating thumbnail for {MediaId} at {ThumbnailPath}", media.Id, thumbnailPath);

        using var image = new MagickImageCollection();

        var originalPath = Path.Join(baseStorageLocation, original.PathRelativeToStorage());
        await image.ReadAsync(originalPath).ConfigureAwait(false);

        if (image.Count < 1)
            throw new InvalidOperationException("No frames found in image for thumbnail generation");

        image.Coalesce();

        foreach (var entry in image)
        {
            entry.AutoOrient();
        }

        if (media.Prime)
        {
            // Prime, so we can just load up the primary file without needing to do anything
        }
        else
        {
            ApplyMediaImageAdjustments(media, image);
        }

        var animated = image.Count > 1;

        // Once loaded, reduce size to the thumbnail size and save it
        foreach (var entry in image)
        {
            ResizeWithDivisibleByTwoDimensions(entry, animated ? AnimatedThumbnailSize : ThumbnailSize);

            // Maybe this is good as Comfy images can embed big workflows
            entry.Strip();
        }

        // Reduce framerate to save on bandwidth
        if (image.Count > 10)
        {
            uint accumulatedDelta = 0;

            for (int i = image.Count - 1; i >= 0; --i)
            {
                if (i % KeepAnimatedThumbnailNthFrame != 0)
                {
                    accumulatedDelta += image[i].AnimationDelay;
                    image.RemoveAt(i);
                }
                else
                {
                    if (accumulatedDelta > 0)
                    {
                        // Apply accumulated delay to a previous kept frame before the deleted ones
                        image[i].AnimationDelay += accumulatedDelta;
                        accumulatedDelta = 0;
                    }
                }
            }
        }

        // Dither gifs
        if (image.Count >= 9 && media.MediaType == MediaType.Gif)
        {
            image.Quantize(new QuantizeSettings
            {
                Colors = 256,
                // or "DitherMethod.No" if you want cleaner/less noisy
                DitherMethod = DitherMethod.Riemersma,
            });
        }

        // Optimize animated images
        if (image.Count > 5)
        {
            // Good default optimize
            image.Optimize();

            // TODO: trigger optimize plus in some cases? (may reduce some image sizes)
            // image.OptimizePlus();
        }

        // Need to make the folder to save in
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath) ??
                                  throw new Exception("Failed to detect target folder"));

        await image.WriteAsync(thumbnailPath).ConfigureAwait(false);

        logger.LogInformation("Generated thumbnail for {MediaId} size is: {Size} KiB", media.Id,
            new FileInfo(thumbnailPath).Length / 1024);
        return thumbnailPath;
    }

    private async Task<string> SmallVideoPath(ConfiguredMedia media, string baseStorageLocation,
        MediaFile original)
    {
        // For efficiency all video thumbnails are webm
        var thumbnailFile = $"{media.Id}.webm";

        // To not pack all thumbnails in the same folder, we use the media ID as a prefix (with a modulo)
        var thumbnailPath = Path.Combine(baseStorageLocation, "thumbnails", $"{media.Id % 100}", thumbnailFile);

        if (File.Exists(thumbnailPath))
        {
            // Exists already, return it
            return thumbnailPath;
        }

        // Need to generate a new thumbnail
        logger.LogInformation("Generating small video for {MediaId} at {ThumbnailPath}", media.Id, thumbnailPath);

        var originalPath = Path.GetFullPath(Path.Join(baseStorageLocation, original.PathRelativeToStorage()));

        var width = original.Width;
        var height = original.Height;

        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(originalPath);

        if (media.Prime)
        {
            // Prime, so we can just load up the primary file without needing to do anything
        }
        else
        {
            // TODO: implement file processing operations
            // This also needs deleting the thumbnail file when settings change
            throw new NotImplementedException();
        }

        var (newWidth, newHeight) = GetDivisibleByTwoDimensions((uint)width, (uint)height, VideoThumbnailSize);

        // Thumbnail doesn't need audio
        startInfo.ArgumentList.Add("-an");

        // And we want only the first video stream
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");

        // And then convert to the target codec
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add(VideoThumbnailCodec);

        // Then resize to the wanted size
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"scale={newWidth}:{newHeight}");

        // And finally, we want to limit the bitrate as the video sizes are tiny so they aren't really legible anyway
        startInfo.ArgumentList.Add("-b:v");
        startInfo.ArgumentList.Add("0.8M");

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(MaxThumbVideoLength.ToString(CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(ThumbnailVideoFrameRate.ToString(CultureInfo.InvariantCulture));

        // Need to make the folder to save in
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath) ??
                                  throw new Exception("Failed to detect target folder"));

        startInfo.ArgumentList.Add(thumbnailPath);

        if (!await ThumbnailProcessingLock.WaitAsync(TimeSpan.FromMinutes(10)))
        {
            logger.LogError("Thumbnail video generation is being overloaded, will cancel a video!");
            throw new InvalidOperationException("Thumbnail video generation is being overloaded");
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);

            if (process == null)
                throw new ApplicationException("Failed to start ffmpeg");

            // Let's try for up to 5 minutes (we shouldn't have super big videos)
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;

            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            ThumbnailProcessingLock.Release();
        }

        if (process.ExitCode != 0)
        {
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;
            var outputText = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            Console.WriteLine(outputText);
            throw new ApplicationException("Failed to run ffmpeg conversion");
        }

        logger.LogInformation("Generated thumb video for {MediaId} size is: {Size} KiB", media.Id,
            new FileInfo(thumbnailPath).Length / 1024);
        return thumbnailPath;
    }

    private async Task WaitForExistingMediaProcessing(ConfiguredMedia media,
        CancellationToken cancellationToken = default)
    {
        // Simple locking based on media ID
        var maxWaitSeconds = 180;
        var waited = 0;

        while (true)
        {
            lock (ActiveMediaProcessingIds)
            {
                if (!ActiveMediaProcessingIds.Contains(media.Id))
                {
                    // We can start processing
                    ActiveMediaProcessingIds.Add(media.Id);
                    break;
                }
            }

            await Task.Delay(1000, cancellationToken);
            ++waited;

            if (waited > maxWaitSeconds)
            {
                logger.LogWarning("Timed out waiting for media {MediaId} to finish processing, will next let job run",
                    media.Id);
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private void ReportMediaProcessingEnd(ConfiguredMedia media)
    {
        lock (ActiveMediaProcessingIds)
        {
            if (!ActiveMediaProcessingIds.Remove(media.Id))
                logger.LogWarning("Media {MediaId} was not found in active processing list for removal", media.Id);
        }
    }
}
