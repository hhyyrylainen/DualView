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

    public static async Task<string?> GetMediaLocalPath(IMediaFile media, IDatabaseService databaseService,
        IDataFolderService dataFolderService, IMediaProcessingService mediaProcessingService)
    {
        var originalMedia = await databaseService.GetMediaByIdAsync(media.Id);

        if (originalMedia == null)
            return null;

        var storage = await GetBaseMediaFolder(databaseService, dataFolderService);

        // Check if we need to return a cropped version
        if (originalMedia.MediaType.IsImage() && NeedsCropping(originalMedia))
        {
            return await mediaProcessingService.ModifiedMediaPath(originalMedia, storage);
        }

        return Path.Join(storage, originalMedia.PathRelativeToStorage());
    }

    private static bool NeedsCropping(MediaFile media)
    {
        return media.CropLeft > 0 || media.CropTop > 0 || media.CropRight > 0 || media.CropBottom > 0;
    }

    public async Task<MediaFile> ImportMedia(string fileName, Stream stream, string? sectionName,
        string? sourcePath = null)
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

            // If it already exists, make sure it is added to the section as desired
            try
            {
                var section = await databaseService.GetOrCreateUploadSectionAsync(sectionName);
                var index = await databaseService.GetNextUploadSectionIndexAsync(section.Id);
                await databaseService.AddMediaToUploadSectionAsync(existing.Id, section.Id, index);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to add media to upload section (it was already imported): {SectionName}",
                    sectionName);
                throw;
            }

            await HandleSourcePath(existing, sourcePath);

            return existing;
        }

        if (!type.IsImage())
        {
            // Video
            var videoMedia = await CreateVideoMedia(stream, fileName, sha3, type);
            videoMedia.IsTemporary = true;

            var result = await SaveFinalMedia(stream, sectionName, storage, videoMedia);

            await HandleSourcePath(result, sourcePath);

            return result;
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

            IsTemporary = true,
        };

        var finalResult = await SaveFinalMedia(stream, sectionName, storage, mediaItem);

        await HandleSourcePath(finalResult, sourcePath);

        return finalResult;
    }

    public async Task<MediaFile> ImportMediaDirect(string fileName, Stream stream, long targetCollectionId,
        long? parentMediaId = null)
    {
        // This variant is used on the server to directly import media to a collection without going through
        // the import window

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

            // If it already exists, we should update the parent media if it wasn't already set
            if (existing.ParentMediaId == null && parentMediaId != null)
            {
                existing.ParentMediaId = parentMediaId;
                await databaseService.SaveMediaFileAsync(existing);
            }

            // But before returning, make sure it is added to the collection as desired
            try
            {
                // We need to calculate the next sequence number
                var sequenceNumber = await GetNextSequenceNumber(targetCollectionId);
                await databaseService.AddMediaToCollection(existing.Id, targetCollectionId, sequenceNumber);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to add media to collection (it was already imported): {CollectionId}",
                    targetCollectionId);
                throw;
            }

            return existing;
        }

        if (!type.IsImage())
        {
            // Video

            var videoMedia = await CreateVideoMedia(stream, fileName, sha3, type);
            videoMedia.ParentMediaId = parentMediaId;

            var result = await SaveFinalMedia(stream, targetCollectionId, storage, videoMedia);

            return result;
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

            ParentMediaId = parentMediaId,
        };

        var finalResult = await SaveFinalMedia(stream, targetCollectionId, storage, mediaItem);

        return finalResult;
    }

    private async Task<int> GetNextSequenceNumber(long collectionId)
    {
        // TODO: implement a method to directly get next sequence number for a collection in the database
        // This is a bit inefficient but for importing it should be fine
        // Ideally IDatabaseService would have a method for this
        var collection = await databaseService.GetCollectionAsync(collectionId);
        if (collection == null)
            return 1;

        return collection.Items.Select(i => i.SequenceNumber).DefaultIfEmpty(0).Max() + 1;
    }

    private async Task<MediaFile> CreateVideoMedia(Stream stream, string fileName, string sha3, MediaType type)
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
        };

        return mediaItem;
    }

    private async Task HandleSourcePath(MediaFile media, string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return;

        var importInfo = await databaseService.GetMediaImportInfoAsync(media.Id);

        if (importInfo == null)
        {
            importInfo = new MediaImportInfo(media.Id, DateTime.UtcNow)
            {
                SourcePath = sourcePath,
            };
        }
        else if (string.IsNullOrEmpty(importInfo.SourcePath))
        {
            importInfo.SourcePath = sourcePath;
        }
        else
        {
            return;
        }

        await databaseService.SaveMediaImportInfoAsync(importInfo);
    }

    private async Task<MediaFile> SaveFinalMedia(Stream stream, string? sectionName, string storage,
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

        // Finally, can save the item (and this adds it to the upload section)
        var newMedia = await databaseService.CreateMediaAsync(mediaItem, sectionName);

        logger.LogInformation("Media imported successfully, id: {Id}", newMedia.Id);
        return newMedia;
    }

    private async Task<MediaFile> SaveFinalMedia(Stream stream, long targetCollectionId, string storage,
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

        // Finally, can save the item (and this adds it to the collection)
        var newMedia = await databaseService.CreateMediaAsync(mediaItem, targetCollectionId);

        logger.LogInformation("Media imported successfully, id: {Id}", newMedia.Id);
        return newMedia;
    }
}
