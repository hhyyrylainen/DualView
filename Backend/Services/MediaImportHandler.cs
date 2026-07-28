using DualView.Shared.Models.Enums;
using Backend.Models;
using ImageMagick;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;

namespace Backend.Services;

public class MediaImportHandler : IMediaImportHandler
{
    private readonly ILogger<MediaImportHandler> logger;
    private readonly IDatabaseService databaseService;
    private readonly IDataFolderService dataFolderService;

    public MediaImportHandler(ILogger<MediaImportHandler> logger, IDatabaseService databaseService,
        IDataFolderService dataFolderService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.dataFolderService = dataFolderService;
    }

    public static async ValueTask<string> GetBaseMediaFolder(IDatabaseService databaseService,
        IDataFolderService dataFolderService, DualViewSettings? settings = null)
    {
        settings ??= await databaseService.GetAppSettingsAsync();

        var storage = settings.LocalMediaStorageLocation;

        if (string.IsNullOrWhiteSpace(storage))
        {
            storage = dataFolderService.GetDataFolderPath();
        }

        return storage;
    }

    public static async Task<string?> GetMediaLocalPath(IConfiguredMediaInfo media, IDatabaseService databaseService,
        IDataFolderService dataFolderService, IMediaProcessingService mediaProcessingService)
    {
        var originalMedia = await databaseService.GetMediaByIdAsync(media.MediaFileId);

        if (originalMedia == null)
            return null;

        var storage = await GetBaseMediaFolder(databaseService, dataFolderService);

        // If this is the prime, return the primary data
        if (media.Prime)
        {
            return Path.Join(storage, originalMedia.PathRelativeToStorage());
        }

        // We need to read some database stuff if we don't have the full info here
        var asConfigured = media as ConfiguredMedia;

        if (asConfigured == null)
        {
            asConfigured = await databaseService.GetConfiguredMediaAsync(media.Id);

            if (asConfigured == null)
                throw new InvalidOperationException("We got a general interface but looking up the main media failed");
        }

        // Otherwise, we need to make sure the processed data exists and then return that
        return await mediaProcessingService.ModifiedMediaPath(asConfigured, originalMedia, storage);
    }

    public async Task<ConfiguredMedia> ImportMedia(string fileName, Stream stream, string targetFolder, bool markAsKeep,
        long? derivedFromId = null)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Current implementation of importing must be able to seek");

        var type = MediaTypeExtensions.TypeFromExtension(Path.GetExtension(fileName));

        var storage = await GetBaseMediaFolder(databaseService, dataFolderService);
        logger.LogInformation("Importing media to {Storage}", storage);
        Directory.CreateDirectory(storage);

        logger.LogInformation("Beginning import checks for {FileName}", fileName);

        // Start by calculating the sha3 hash of the file so that we can check for duplicates
        stream.Position = 0;
        var hashBytes = await SHA3_256.HashDataAsync(stream);
        var sha3 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var existing = await databaseService.GetMediaByHashAsync(sha3);

        if (existing != null)
        {
            logger.LogInformation("Hash is already imported: {Hash}", sha3);

            // If it already exists, we should update the derived from if it wasn't already set
            if (existing.DerivedFromImageId == null && derivedFromId != null)
            {
                existing.DerivedFromImageId = derivedFromId;
                await databaseService.SaveMediaFileAsync(existing);
            }

            // Get the prime to return
            var prime = await databaseService.GetConfiguredMediaPrimeAsync(existing.Id);

            // But before returning, make sure it is added to the folder as desired
            try
            {
                await databaseService.AddMediaToFolder(prime.Id, targetFolder, false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to add media to folder (it was already imported): {Folder}", targetFolder);
                throw;
            }

            return prime;
        }

        if (!type.IsImage())
        {
            // Video

            var videoMedia = await CreateVideoMedia(stream, fileName, sha3, type, markAsKeep);
            videoMedia.DerivedFromImageId = derivedFromId;

            var videoPrime = await SaveFinalMedia(stream, targetFolder, storage, videoMedia);

            return videoPrime;
        }

        using var frames = new MagickImageCollection();

        try
        {
            stream.Position = 0;
            await frames.ReadAsync(stream);

            if (frames.Count < 1)
                throw new ArgumentException("No frames found in image");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to read image frames");
            throw;
        }

        if (frames.Count > 1)
        {
            type = type.MakeAnimated();

            // Safety fallback
            if (frames[0].AnimationDelay <= 0)
                frames[0].AnimationDelay = 1;
        }

        var mediaItem = new MediaFile(fileName, sha3)
        {
            Width = (int)frames[0].Width,
            Height = (int)frames[0].Height,
            FrameCount = frames.Count,

            // Assume uniform duration
            FramesPerSecond = frames.Count > 1 ? 100.0f / frames[0].AnimationDelay : -1,
            MediaType = type,

            Keep = markAsKeep,
            DerivedFromImageId = derivedFromId,
        };

        var newPrime = await SaveFinalMedia(stream, targetFolder, storage, mediaItem);

        return newPrime;
    }

    private async Task<MediaFile> CreateVideoMedia(Stream stream, string fileName, string sha3, MediaType type,
        bool markAsKeep)
    {
        // We need to use ffprobe on the file to get its info

        FileProbe.MediaFileInfo fileInfo;

        var tempFile = Path.GetTempFileName();
        try
        {
            // So unfortunately, we need to copy the stream to a temporary file to run it.
            // This has to be in a block so that the entire thing is actually written to disk and can be inspected.
            {
                await using var writer = File.Create(tempFile);
                stream.Position = 0;
                await stream.CopyToAsync(writer);
            }

            var targetLength = stream.Length;
            var actualSize = new FileInfo(tempFile).Length;
            if (targetLength != actualSize)
            {
                throw new IOException(
                    $"Failed to write file to disk (should have read {targetLength} but actual size: {actualSize})");
            }

            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                fileInfo = await FileProbe.ProbeVideoAsync(tempFile, timeout.Token);
            }
            catch (Exception e)
            {
                // TODO: we need to do something for "piped webp sequence"
                throw new IOException("Failed to probe file for media details", e);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }

        var video = fileInfo.VideoStream ?? throw new Exception("No video stream found");

        // TODO: rounding?
        var expectedFrames = (int)(fileInfo.Duration * video.FramesPerSecond);

        var mediaItem = new MediaFile(fileName, sha3)
        {
            Width = video.Width,
            Height = video.Height,
            FrameCount = expectedFrames,

            FramesPerSecond = (float)video.FramesPerSecond,
            MediaType = type,

            Keep = markAsKeep,
        };

        return mediaItem;
    }

    private async Task<ConfiguredMedia> SaveFinalMedia(Stream stream, string targetFolder, string storage,
        MediaFile mediaItem)
    {
        var finalPath = Path.Join(storage, mediaItem.PathRelativeToStorage());

        logger.LogInformation("Saving media to {Path}", finalPath);

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ??
                                  throw new Exception("Failed to detect target folder"));

        // Now we can finally write the original file
        await using var writer = File.Create(finalPath);
        stream.Position = 0;
        await stream.CopyToAsync(writer);

        // Finally, can save the item (and this puts it in the folder as well)
        var newPrime = await databaseService.CreateMediaAsync(mediaItem, targetFolder, false);

        logger.LogInformation("Media imported successfully, prime config: {Id}", newPrime.Id);
        return newPrime;
    }
}
