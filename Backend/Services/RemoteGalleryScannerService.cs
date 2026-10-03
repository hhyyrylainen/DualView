using System.Text.Json;
using System.Threading.Channels;
using Backend.Models;
using Backend.Plugins;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Owns the durable, one-at-a-time scan queue for remote galleries.
/// </summary>
public sealed class RemoteGalleryScannerService : IRemoteGalleryScannerService, IDisposable
{
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IRemoteScanService remoteScanService;
    private readonly IRemoteDownloadProvider downloadProvider;
    private readonly IPluginRegistry pluginRegistry;
    private readonly ILogger<RemoteGalleryScannerService> logger;
    private readonly IRemoteGalleryMediaService remoteGalleryMediaService;
    private readonly IAppEvents appEvents;
    private readonly Channel<long> galleryQueue = Channel.CreateUnbounded<long>();
    private readonly Channel<GalleryAcceptRequest> galleryAcceptQueue = Channel.CreateUnbounded<GalleryAcceptRequest>();
    private readonly CancellationTokenSource shutdownCancellationSource = new();
    private readonly HashSet<long> acceptingGalleryIds = new();
    private readonly Lock acceptingGalleryLock = new();

    private readonly Task workerTask;
    private readonly Task acceptWorkerTask;
    private int stopped;

    public RemoteGalleryScannerService(IServiceScopeFactory serviceScopeFactory, IRemoteScanService remoteScanService,
        IPluginRegistry pluginRegistry, IAppEvents appEvents, ILogger<RemoteGalleryScannerService> logger,
        IRemoteGalleryMediaService remoteGalleryMediaService, IRemoteDownloadProvider downloadProvider)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.remoteScanService = remoteScanService;
        this.pluginRegistry = pluginRegistry;
        this.logger = logger;
        this.remoteGalleryMediaService = remoteGalleryMediaService;
        this.appEvents = appEvents;
        this.downloadProvider = downloadProvider;

        appEvents.ScannedCollectionCreated += QueueGallery;
        workerTask = Task.Run(async () =>
        {
            await PauseInterruptedScansAsync();
            await ProcessQueueAsync();
        });

        acceptWorkerTask = Task.Run(ProcessAcceptQueueAsync);
    }

    public async Task<long> CreateGalleryAsync(RemoteDownloadRequest request, CancellationToken cancellationToken)
    {
        var plugin = await FindGalleryPluginAsync(request, cancellationToken) ??
                     throw new ArgumentException("Unsupported gallery website", nameof(request));
        var canonicalUrl = plugin.ConvertToCanonicalUrl(request.HtmlUrl);

        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var existing = await database.GetScannedCollectionByCanonicalUrlAsync(canonicalUrl);
        if (existing != null)
            return existing.Id;

        var initialGalleryName = plugin is IRemoteGalleryNameFilter nameFilter
            ? nameFilter.FilterGalleryName(request.PageTitle) ?? request.PageTitle
            : request.PageTitle;
        var gallery = new ScannedCollection(initialGalleryName)
        {
            GalleryUrl = request.HtmlUrl,
            CanonicalUrl = canonicalUrl,
            CurrentlyScannedUrl = request.HtmlUrl,
            ScannerState = JsonSerializer.Serialize(new GalleryScanState { PendingPages = [request], }),
        };

        await database.CreateScannedCollectionAsync(gallery);

        appEvents.NotifyScannedCollectionCreated(gallery.Id);
        await NotifyScannedCollectionUpdatedAsync(gallery.Id);
        return gallery.Id;
    }

    public async Task RetryGalleryAsync(long galleryId, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId) ??
                      throw new ArgumentException("Gallery not found", nameof(galleryId));

        gallery.ScanFailed = false;
        gallery.ScanError = null;
        gallery.ScanPaused = false;
        await database.SaveScannedCollectionAsync(gallery);

        appEvents.NotifyScannedCollectionCreated(galleryId);
        await NotifyScannedCollectionUpdatedAsync(galleryId);
    }

    public async Task PauseGalleryAsync(long galleryId, CancellationToken cancellationToken)
    {
        await SetPauseStateAsync(galleryId, true);
    }

    public async Task ResumeGalleryAsync(long galleryId, CancellationToken cancellationToken)
    {
        await SetPauseStateAsync(galleryId, false);
    }

    public async Task DeleteGalleryAsync(long galleryId, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId);
        if (gallery == null)
        {
            logger.LogWarning("Tried to delete already missing remote gallery {GalleryId}", galleryId);
            return;
        }

        await database.DeleteScannedCollectionAsync(gallery);
        logger.LogInformation("Deleted remote gallery scan {GalleryId}", galleryId);
        await NotifyScannedCollectionUpdatedAsync(galleryId);
    }

    public async Task AcceptGalleryAsync(long galleryId, string name, IReadOnlyCollection<long>? mediaIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A target import section name is required", nameof(name));

        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        _ = await database.GetScannedCollectionAsync(galleryId) ??
            throw new ArgumentException("Gallery not found", nameof(galleryId));

        lock (acceptingGalleryLock)
        {
            if (!acceptingGalleryIds.Add(galleryId))
                return;
        }

        if (galleryAcceptQueue.Writer.TryWrite(new GalleryAcceptRequest(galleryId, name, mediaIds?.ToArray())))
        {
            return;
        }

        lock (acceptingGalleryLock)
        {
            acceptingGalleryIds.Remove(galleryId);
        }

        throw new InvalidOperationException("The scanned gallery accept queue is stopping");
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
            return;

        galleryQueue.Writer.TryComplete();
        galleryAcceptQueue.Writer.TryComplete();
        shutdownCancellationSource.CancelAfter(timeout);
        try
        {
            await Task.WhenAll(workerTask, acceptWorkerTask).WaitAsync(timeout + TimeSpan.FromSeconds(1));
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Remote gallery scanner did not stop within {Timeout}", timeout);
        }
    }

    public void Dispose()
    {
        StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        shutdownCancellationSource.Dispose();
    }

    private void QueueGallery(long galleryId)
    {
        galleryQueue.Writer.TryWrite(galleryId);
    }

    private async Task PauseInterruptedScansAsync()
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var galleries = await database.GetScannedCollectionsAsync();

        foreach (var gallery in galleries.Where(gallery => gallery.CurrentlyScannedUrl != null &&
                                                           !gallery.ScanPaused && !gallery.ScanFailed))
        {
            gallery.ScanPaused = true;
            gallery.ScanError = "Paused due to server restart";
            await database.SaveScannedCollectionAsync(gallery);
            logger.LogInformation("Paused remote gallery scan {GalleryId} after server restart", gallery.Id);
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var galleryId in galleryQueue.Reader.ReadAllAsync(shutdownCancellationSource.Token))
        {
            try
            {
                await ScanGalleryAsync(galleryId, shutdownCancellationSource.Token);
            }
            catch (OperationCanceledException) when (shutdownCancellationSource.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                logger.LogError(e, "Remote gallery scanner failed for gallery {GalleryId}", galleryId);
            }
        }
    }

    private async Task ProcessAcceptQueueAsync()
    {
        await foreach (var request in galleryAcceptQueue.Reader.ReadAllAsync(shutdownCancellationSource.Token))
        {
            try
            {
                await ProcessAcceptGalleryAsync(request.GalleryId, request.Name, request.MediaIds,
                    shutdownCancellationSource.Token);
            }
            catch (OperationCanceledException) when (shutdownCancellationSource.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Accepting scanned gallery {GalleryId} failed", request.GalleryId);
            }
            finally
            {
                lock (acceptingGalleryLock)
                {
                    acceptingGalleryIds.Remove(request.GalleryId);
                }
            }
        }
    }

    private async Task ProcessAcceptGalleryAsync(long galleryId, string name, IReadOnlyCollection<long>? mediaIds,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var mediaImportHandler = scope.ServiceProvider.GetRequiredService<IMediaImportHandler>();
        var tagParser = scope.ServiceProvider.GetRequiredService<ITagParser>();
        var missingTagService = scope.ServiceProvider.GetRequiredService<IMissingTagService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId) ??
                      throw new ArgumentException("Gallery not found", nameof(galleryId));
        var uploadSection = await database.GetOrCreateUploadSectionAsync(name);

        foreach (var tag in gallery.UnrecognizedTags.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parsedTag = await tagParser.ParseTag(tag);
            if (parsedTag == null)
            {
                logger.LogWarning("Could not parse scanned gallery tag '{Tag}' for gallery {GalleryId}", tag,
                    galleryId);
                await missingTagService.ReportTagAsync(tag, MissingTagTarget.UploadSection, uploadSection.Id);
                continue;
            }

            await database.AddParsedAppliedTagToUploadSectionAsync(uploadSection.Id, parsedTag.GetDTO());
        }

        var selected = gallery.FoundMedia
            .Where(item => mediaIds?.Contains(item.Id) ?? item.Selected)
            .ToList();
        if (selected.Count == 0)
        {
            await DeleteEmptyAcceptedGalleryAsync(galleryId);
            return;
        }

        var downloadGalleryId = await GetOrCreateDownloadGalleryAsync(database, gallery);

        foreach (var item in selected)
        {
            var path = await remoteGalleryMediaService.GetFullMediaAsync(item.Id, cancellationToken);
            await using var stream = File.OpenRead(path);
            var media = await mediaImportHandler.ImportMedia(GetImportFileName(item), stream, uploadSection.Name);
            await SaveMediaImportInfoAsync(database, media.Id, item, downloadGalleryId);

            var parsedTags = new List<AppliedTagDTO>();
            foreach (var tag in item.UnparsedTags)
            {
                var parsedTag = await tagParser.ParseTag(tag);
                if (parsedTag == null)
                {
                    await missingTagService.ReportTagAsync(tag, MissingTagTarget.MediaFile, media.Id);
                    continue;
                }

                parsedTags.Add(parsedTag.GetDTO());
            }

            if (parsedTags.Count > 0)
                await database.AddParsedAppliedTagsToMediaAsync([media.Id], parsedTags);

            gallery.FoundMedia.Remove(item);
            await database.SaveScannedCollectionAsync(gallery);
            await NotifyScannedCollectionUpdatedAsync(galleryId);
        }

        await DeleteEmptyAcceptedGalleryAsync(galleryId);
    }

    private static async Task<long> GetOrCreateDownloadGalleryAsync(IDatabaseService database,
        ScannedCollection scannedGallery)
    {
        var downloadGallery = await database.GetDownloadGalleryByCanonicalUrlAsync(scannedGallery.CanonicalUrl);
        if (downloadGallery == null)
        {
            downloadGallery = await database.GetDownloadGalleryByUrlAsync(scannedGallery.GalleryUrl);
            if (downloadGallery != null && downloadGallery.CanonicalUrl != scannedGallery.CanonicalUrl)
            {
                downloadGallery.CanonicalUrl = scannedGallery.CanonicalUrl;
                await database.SaveAsync();
            }
        }

        var downloadGalleryId = downloadGallery?.Id ??
                                await database.CreateDownloadGalleryAsync(scannedGallery.GalleryUrl,
                                    scannedGallery.CanonicalUrl);

        await database.UpdateDownloadGalleryAsync(downloadGalleryId, null, scannedGallery.Name, null,
            scannedGallery.UnrecognizedTags);

        return downloadGalleryId;
    }

    private static async Task SaveMediaImportInfoAsync(IDatabaseService database, long mediaId, FoundMedia item,
        long downloadGalleryId)
    {
        var importInfo = await database.GetMediaImportInfoAsync(mediaId) ?? new MediaImportInfo(mediaId);
        if (string.IsNullOrWhiteSpace(importInfo.SourceUrl))
            importInfo.SourceUrl = item.DownloadUrl;

        importInfo.Referrer = item.Referrer;
        importInfo.PreferredName = item.OverrideName;
        importInfo.TagsString = string.Join(", ", item.UnparsedTags);
        importInfo.DownloadGalleryId = downloadGalleryId;
        await database.SaveMediaImportInfoAsync(importInfo);
    }

    private async Task DeleteEmptyAcceptedGalleryAsync(long galleryId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId);
        if (gallery == null)
            return;

        var hasPendingScanPages = DeserializeState(gallery.ScannerState).PendingPages.Count > 0;
        if (gallery.FoundMedia.Count > 0 || gallery.CurrentlyScannedUrl != null || hasPendingScanPages)
            return;

        await database.DeleteScannedCollectionAsync(gallery);
        logger.LogInformation("Deleted empty accepted scanned gallery {GalleryId}", galleryId);
        await NotifyScannedCollectionUpdatedAsync(galleryId);
    }

    private async Task ScanGalleryAsync(long galleryId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            var gallery = await database.GetScannedCollectionAsync(galleryId);
            if (gallery == null)
            {
                logger.LogWarning("Stopping remote gallery scan {GalleryId}: target no longer exists", galleryId);
                return;
            }

            if (gallery.ScanFailed || gallery.ScanPaused)
                return;

            var state = DeserializeState(gallery.ScannerState);
            if (state.PendingPages.Count == 0)
            {
                gallery.CurrentlyScannedUrl = null;
                await database.SaveScannedCollectionAsync(gallery);
                return;
            }

            var page = state.PendingPages[0];
            gallery.CurrentlyScannedUrl = page.HtmlUrl;
            await database.SaveScannedCollectionAsync(gallery);

            PageScanResult result;
            try
            {
                result = await remoteScanService.ScanContentPage(page, false, cancellationToken);
                if (result.Subpages.Count == 0 && result.Content.Count == 0)
                    throw new InvalidOperationException("The page scan returned no subpages or content");
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                await MarkGalleryFailedAsync(galleryId, e.Message);
                return;
            }

            try
            {
                await SavePageResultAsync(database, galleryId, page, result, cancellationToken);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                await remoteScanService.RecordEventAsync(new RemoteScanEvent
                {
                    EventType = "scan-failed",
                    ImageUrl = page.HtmlUrl,
                    ReferrerUrl = page.Referrer,
                    Detail = e.Message,
                }, cancellationToken);
                await MarkGalleryFailedAsync(galleryId, e.Message);
                return;
            }

            await remoteScanService.RecordEventAsync(new RemoteScanEvent
            {
                EventType = "scan-succeeded",
                ImageUrl = page.HtmlUrl,
                ReferrerUrl = page.Referrer,
            }, cancellationToken);

            if (result.AdditionalWaitTime > TimeSpan.Zero)
                await Task.Delay(result.AdditionalWaitTime, cancellationToken);
        }
    }

    private async Task SavePageResultAsync(IDatabaseService database, long galleryId, RemoteDownloadRequest page,
        PageScanResult result,
        CancellationToken cancellationToken)
    {
        var gallery = await database.GetScannedCollectionAsync(galleryId);
        if (gallery == null)
        {
            logger.LogWarning("Discarding scan results for deleted remote gallery {GalleryId}", galleryId);
            return;
        }

        logger.LogInformation(
            "Finished scanning {Url} for remote gallery {GalleryId}, found {Count1} content item(s) and " +
            "{Count2} subpage(s)", page.HtmlUrl, galleryId, result.Content.Count, result.Subpages.Count);

        var state = DeserializeState(gallery.ScannerState);
        state.PendingPages.RemoveAll(candidate => candidate.HtmlUrl == page.HtmlUrl);
        var collectionContentChanged = false;

        var plugin = await FindGalleryPluginAsync(page, cancellationToken) ??
                     throw new InvalidOperationException("Plugin disappeared while scanning");
        var currentCanonicalUrl = plugin.ConvertToCanonicalUrl(page.HtmlUrl);
        state.SeenUrls.Add(currentCanonicalUrl);

        var pendingPages = new List<(RemoteDownloadRequest Page, bool IsContentPage)>();
        var queuedCanonicalUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pendingPage in state.PendingPages)
        {
            var canonicalUrl = plugin.ConvertToCanonicalUrl(pendingPage.HtmlUrl);
            if (canonicalUrl == currentCanonicalUrl || !queuedCanonicalUrls.Add(canonicalUrl))
                continue;

            var information = await plugin.InspectWebsiteRequest(pendingPage, downloadProvider, cancellationToken);
            var isContentPage = IsContentPage(information);

            state.SeenUrls.Add(canonicalUrl);
            pendingPages.Add((pendingPage, isContentPage));
        }

        var discoveredPages = new List<(RemoteDownloadRequest Page, bool IsContentPage)>();
        foreach (var subpage in result.Subpages)
        {
            var canonicalUrl = plugin.ConvertToCanonicalUrl(subpage.HtmlUrl);
            if (queuedCanonicalUrls.Contains(canonicalUrl) || state.SeenUrls.Contains(canonicalUrl))
                continue;

            var information = await plugin.InspectWebsiteRequest(subpage, downloadProvider, cancellationToken);
            var isContentPage = IsContentPage(information);

            if (!isContentPage)
                logger.LogInformation("Found new gallery sub-page: {Url}", subpage.HtmlUrl);

            queuedCanonicalUrls.Add(canonicalUrl);
            state.SeenUrls.Add(canonicalUrl);
            discoveredPages.Add((subpage, isContentPage));
        }

        // Sort found pages for optimal scan order
        var orderedPages = pendingPages.Concat(discoveredPages);
        if (plugin.ScanContentPagesFirst)
            orderedPages = orderedPages.OrderBy(candidate => candidate.IsContentPage ? 0 : 1);

        state.PendingPages = orderedPages
            .Select(candidate => candidate.Page)
            .ToList();

        foreach (var content in result.Content)
        {
            var canonicalUrl = plugin.ConvertToCanonicalUrl(content.ImageUrl);
            if (gallery.FoundMedia.All(item => item.CanonicalUrl != canonicalUrl))
            {
                gallery.FoundMedia.Add(new FoundMedia(content.ImageUrl)
                {
                    CanonicalUrl = canonicalUrl,
                    Referrer = content.Referrer,
                    ThumbnailUrl = content.Thumbnail?.Url,
                    ThumbnailCropX = content.Thumbnail?.Crop?.X,
                    ThumbnailCropY = content.Thumbnail?.Crop?.Y,
                    ThumbnailCropWidth = content.Thumbnail?.Crop?.Width,
                    ThumbnailCropHeight = content.Thumbnail?.Crop?.Height,
                    UnparsedTags = content.Tags.ToList(),
                    MediaType = content.MediaType ?? TryGetMediaType(content.ImageUrl),
                    Cookies = JsonSerializer.Serialize(content.Cookies),
                    ImpersonationHeaders = JsonSerializer.Serialize(content.ImpersonationHeaders),
                    OverrideName = content.OverrideName,
                });
                collectionContentChanged = true;
            }
        }

        if (result.GalleryInfo != null)
        {
            collectionContentChanged |= result.GalleryInfo.GalleryName != null &&
                                        result.GalleryInfo.GalleryName != gallery.Name;
            gallery.Name = result.GalleryInfo.GalleryName ?? gallery.Name;
            gallery.UnrecognizedTags = result.GalleryInfo.TagsString ?? gallery.UnrecognizedTags;
        }

        gallery.ScannerState = JsonSerializer.Serialize(state);

        logger.LogDebug(
            "Scanner state is {Kb} KiB for gallery {Name} ({Id}), with pending scan queue size: {PendingPagesCount}",
            gallery.ScannerState.Length / 1024, gallery.Name, gallery.Id, state.PendingPages.Count);

        gallery.CurrentlyScannedUrl = state.PendingPages.FirstOrDefault()?.HtmlUrl;
        try
        {
            await database.SaveScannedCollectionAsync(gallery);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogError(
                "Discarding scan results for remote gallery {GalleryId} deleted while saving (or another DB conflict)",
                galleryId);
            return;
        }

        if (collectionContentChanged)
            await NotifyScannedCollectionUpdatedAsync(galleryId);

        foreach (var item in gallery.FoundMedia.Where(item => string.IsNullOrWhiteSpace(item.LocalThumbnailFilePath)))
        {
            try
            {
                await remoteGalleryMediaService.GetThumbnailAsync(item.Id, cancellationToken);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                // A preview failure must not throw away an otherwise valid page scan.
                logger.LogWarning(e, "Failed to cache thumbnail for remote gallery item {ItemId}", item.Id);
            }
        }
    }

    private async Task MarkGalleryFailedAsync(long galleryId, string error)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId);
        if (gallery == null)
            return;

        gallery.ScanFailed = true;
        gallery.ScanError = error;

        await database.SaveScannedCollectionAsync(gallery);
        logger.LogWarning("Gallery {GalleryId} scan stopped: {Error}", galleryId, error);
    }

    private async Task SetPauseStateAsync(long galleryId, bool paused)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var gallery = await database.GetScannedCollectionAsync(galleryId) ??
                      throw new ArgumentException("Gallery not found", nameof(galleryId));

        gallery.ScanPaused = paused;
        if (!paused)
        {
            gallery.ScanFailed = false;
            gallery.ScanError = null;
        }

        await database.SaveScannedCollectionAsync(gallery);
        if (!paused)
            appEvents.NotifyScannedCollectionCreated(galleryId);
        await NotifyScannedCollectionUpdatedAsync(galleryId);
    }

    private async Task NotifyScannedCollectionUpdatedAsync(long galleryId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IEntityUpdateNotifier>();
        await notifier.NotifyScannedCollectionUpdated(galleryId);
    }

    private static string GetImportFileName(FoundMedia item)
    {
        if (!string.IsNullOrWhiteSpace(item.OverrideName) && Path.GetExtension(item.OverrideName).Length > 0)
            return item.OverrideName;

        if (Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var uri) &&
            Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } fileName)
        {
            return fileName;
        }

        // NOTE: jpg probably fails if it is not actually a jpeg, so we kind of would have to download the item here
        // already and use ImageMagick to inspect what the media actually is
        return item.MediaType.HasValue
            ? $"remote_{item.Id}{item.MediaType.Value.ExtensionFromType()}"
            : $"remote_{item.Id}.jpg";
    }

    private static MediaType? TryGetMediaType(string url)
    {
        try
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            return MediaTypeExtensions.TypeFromExtension(Path.GetExtension(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<IRemoteDownloadPlugin?> FindGalleryPluginAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var plugin in pluginRegistry.GetRemoteDownloadPlugins())
        {
            var information = await plugin.InspectWebsiteRequest(request, downloadProvider, cancellationToken);
            if ((information & (UrlInformation.Gallery | UrlInformation.GalleryPage | UrlInformation.ContentPage)) != 0)
                return plugin;
        }

        return null;
    }

    private static bool IsContentPage(UrlInformation information)
    {
        return (information & UrlInformation.ContentPage) != 0 &&
               (information & UrlInformation.GalleryPage) == 0;
    }

    private static GalleryScanState DeserializeState(string state)
    {
        return string.IsNullOrWhiteSpace(state)
            ? new GalleryScanState()
            : JsonSerializer.Deserialize<GalleryScanState>(state) ?? new GalleryScanState();
    }

    private sealed class GalleryScanState
    {
        public List<RemoteDownloadRequest> PendingPages { get; set; } = new();
        public HashSet<string> SeenUrls { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record GalleryAcceptRequest(long GalleryId, string Name, long[]? MediaIds);
}
