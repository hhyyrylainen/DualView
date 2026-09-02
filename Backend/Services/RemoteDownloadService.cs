using System.Net;
using System.Text;
using System.Threading.Channels;
using Backend.Models;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Downloads remote media sequentially and imports it into upload sections.
/// </summary>
public sealed class RemoteDownloadService : IRemoteDownloadService
{
    private const int MaximumAttempts = 10;
    private const int MaximumCachedUrls = 1000;

    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IRemoteScanService remoteScanService;
    private readonly ILogger<RemoteDownloadService> logger;
    private readonly ICurlDownloadService curlDownloadService;
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private readonly Channel<RemoteDownloadRequest> downloadQueue = Channel.CreateUnbounded<RemoteDownloadRequest>();
    private readonly HttpClient httpClient;
    private readonly Dictionary<string, long> recentDownloadUrls = new(StringComparer.Ordinal);

    private CancellationTokenSource? cancellationTokenSource;
    private Task? processingTask;
    private bool running;

    public RemoteDownloadService(IServiceScopeFactory serviceScopeFactory,
        IRemoteScanService remoteScanService, ILogger<RemoteDownloadService> logger,
        ICurlDownloadService curlDownloadService)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.remoteScanService = remoteScanService;
        this.logger = logger;
        this.curlDownloadService = curlDownloadService;

        var httpHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingDelay = TimeSpan.FromMinutes(2),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            UseCookies = false,
        };

        // Disable HTTPS checking as we basically want files always
        httpHandler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        httpClient = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
    }

    public void Start()
    {
        if (running)
            return;

        running = true;
        cancellationTokenSource = new CancellationTokenSource();
        processingTask = Task.Run(() => RunDownloadThreadAsync(cancellationTokenSource.Token));
        logger.LogDebug("Remote download service started");
    }

    public void Stop(bool wait, TimeSpan timeout)
    {
        if (!running)
            return;

        running = false;
        cancellationTokenSource?.Cancel();

        if (wait && processingTask != null && !processingTask.Wait(timeout))
            logger.LogWarning("Remote download service did not stop within {Timeout}", timeout);

        if (processingTask?.IsCompleted == true)
        {
            processingTask = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }

        logger.LogDebug("Remote download service stopped");
    }

    public async ValueTask QueueDownloadAsync(RemoteDownloadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            throw new ArgumentException("A remote download requires an image URL", nameof(request));

        if (request.ImageUrl.Equals(request.Referrer, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A remote download cannot have the same URL as the referrer", nameof(request));

        await remoteScanService.RecordEventAsync(new RemoteScanEvent
        {
            EventType = "download-queued",
            ImageUrl = request.ImageUrl,
        }, cancellationToken);
        await downloadQueue.Writer.WriteAsync(request, cancellationToken);
    }

    private async Task RunDownloadThreadAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // This loops normally forever if there are no problems
                await foreach (var request in downloadQueue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessDownloadAsync(request, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        logger.LogInformation("Stopping remote download processing");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Remote download worker failed for {ImageUrl}", request.ImageUrl);

                        await remoteScanService.RecordEventAsync(new RemoteScanEvent
                        {
                            EventType = "download-worker-fail",
                            ImageUrl = request.ImageUrl,
                            Detail = ex.ToString(),
                        }, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Remote download queue stopped");
                break;
            }

            // This is reached only if an exception happened
            if (stoppingToken.IsCancellationRequested)
                break;

            // Restart worker every 15 seconds
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ProcessDownloadAsync(RemoteDownloadRequest request, CancellationToken cancellationToken)
    {
        await processingLock.WaitAsync(cancellationToken);
        try
        {
            var enrichedRequest = request;
            var enrichmentCompleted = false;
            for (var attempt = 1; attempt <= MaximumAttempts; ++attempt)
            {
                try
                {
                    if (!enrichmentCompleted)
                    {
                        enrichedRequest = await remoteScanService.EnrichDownloadAsync(request, cancellationToken);
                        enrichmentCompleted = true;
                    }

                    await DownloadAndImportAsync(enrichedRequest, cancellationToken);
                    await remoteScanService.RecordEventAsync(new RemoteScanEvent
                    {
                        EventType = "download-succeeded",
                        ImageUrl = enrichedRequest.ImageUrl,
                        Attempt = attempt,
                    }, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await remoteScanService.RecordEventAsync(new RemoteScanEvent
                    {
                        EventType = attempt == MaximumAttempts ? "download-failed" : "download-retry",
                        ImageUrl = request.ImageUrl,
                        Detail = ex.Message,
                        Attempt = attempt,
                    }, cancellationToken);

                    if (attempt == MaximumAttempts)
                    {
                        logger.LogError(ex, "Remote download failed after {AttemptCount} attempts for {ImageUrl}",
                            MaximumAttempts, request.ImageUrl);
                        return;
                    }

                    var delaySeconds = Math.Min(300, 3 * Math.Pow(2, attempt - 1));
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
        }
        finally
        {
            processingLock.Release();
        }
    }

    private async Task DownloadAndImportAsync(RemoteDownloadRequest request, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var tagParser = scope.ServiceProvider.GetRequiredService<ITagParser>();
        var missingTagService = scope.ServiceProvider.GetRequiredService<IMissingTagService>();
        var section = await databaseService.GetOrCreateUploadSectionAsync(request.TargetImportSection);

        var cachedMediaId = GetCachedMediaId(request);
        if (cachedMediaId.HasValue)
        {
            var cachedMedia = await databaseService.GetMediaByIdIncludingDeletedAsync(cachedMediaId.Value);
            if (cachedMedia != null)
            {
                if (cachedMedia.IsDeleted)
                    await databaseService.RestoreMediaAsync(cachedMedia.Id);

                await databaseService.SetUploadSectionActiveAsync(section.Id);
                await databaseService.AddMediaToUploadSectionAsync(cachedMedia.Id, section.Id,
                    await databaseService.GetNextUploadSectionIndexAsync(section.Id));
                await ApplyDownloadMetadataAsync(cachedMedia, request, databaseService, tagParser, missingTagService);
                return;
            }

            RemoveCachedUrl(request);
        }

        var settings = await databaseService.GetAppSettingsAsync();
        string? curlOutputPath = null;
        try
        {
            Stream mediaStream;
            if (settings.UseCurlForRemoteDownloads)
            {
                var referrer = Uri.TryCreate(request.Referrer, UriKind.Absolute, out var parsedReferrer)
                    ? parsedReferrer.ToString()
                    : null;
                curlOutputPath = await curlDownloadService.DownloadAsync(request.ImageUrl,
                    new BrowserImpersonationHeaders(request.ImpersonationHeaders).PassedHeaders(), referrer,
                    request.Cookies, cancellationToken);
                mediaStream = File.OpenRead(curlOutputPath);
            }
            else
            {
                mediaStream = await DownloadWithHttpClientAsync(request, cancellationToken);
            }

            await using (mediaStream)
            {
                var mediaImportHandler = scope.ServiceProvider.GetRequiredService<IMediaImportHandler>();

                // Make sure there is an active upload section to avoid images bundling up as separate sections if we
                // have a long queue
                await databaseService.SetUploadSectionActiveAsync(section.Id);

                MediaFile media;
                try
                {
                    media = await mediaImportHandler.ImportMedia(GetImportFileName(request), mediaStream, section.Name);
                }
                catch (Exception)
                {
                    // Try to read the first 200 characters as text from the stream
                    mediaStream.Position = 0;

                    using var reader = new StreamReader(mediaStream, Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 1024, leaveOpen: true);

                    var buffer = new char[200];
                    var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                    var startOfResponse = new string(buffer, 0, charsRead);

                    logger.LogError("Cannot decode downloaded data as an image: {StartOfResponse}", startOfResponse);
                    throw;
                }

                await ApplyDownloadMetadataAsync(media, request, databaseService, tagParser, missingTagService);
                AddCachedUrl(request, media.Id);
            }
        }
        finally
        {
            if (curlOutputPath != null)
            {
                try
                {
                    File.Delete(curlOutputPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete cURL download file {Path}", curlOutputPath);
                }
            }
        }
    }

    private async Task<Stream> DownloadWithHttpClientAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.ImageUrl);
        new BrowserImpersonationHeaders(request.ImpersonationHeaders).ConfigureHttpRequest(httpRequest, false);

        if (!string.IsNullOrWhiteSpace(request.Referrer) && Uri.TryCreate(request.Referrer, UriKind.Absolute,
                out var referrer))
        {
            httpRequest.Headers.Referrer = referrer;
        }

        if (request.Cookies.Count > 0)
        {
            var cookieHeader = string.Join("; ", request.Cookies.Select(cookie =>
                $"{cookie.Key}={cookie.Value}"));
            httpRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        // Host should be automatic, so don't set it manually (which would break redirects)
        // httpRequest.Headers.Host = new Uri(request.ImageUrl).Host;

        // If we want to always close the connections:
        // httpRequest.Headers.ConnectionClose = true;

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var mediaStream = new MemoryStream();
        try
        {
            await responseStream.CopyToAsync(mediaStream, cancellationToken);
            mediaStream.Position = 0;
            return mediaStream;
        }
        catch
        {
            await mediaStream.DisposeAsync();
            throw;
        }
    }

    private async Task ApplyDownloadMetadataAsync(MediaFile media, RemoteDownloadRequest request,
        IDatabaseService databaseService, ITagParser tagParser, IMissingTagService missingTagService)
    {
        var importInfo = await databaseService.GetMediaImportInfoAsync(media.Id) ?? new MediaImportInfo(media.Id);
        if (string.IsNullOrWhiteSpace(importInfo.SourceUrl))
            importInfo.SourceUrl = request.ImageUrl;
        importInfo.Referrer = request.Referrer;
        importInfo.PreferredName = request.OverrideName;
        importInfo.TagsString = string.Join(", ", request.Tags);
        importInfo.DownloadGalleryId = request.DownloadGalleryId.HasValue &&
                                       await databaseService.GetDownloadGalleryAsync(request.DownloadGalleryId.Value) !=
                                       null
            ? request.DownloadGalleryId
            : null;
        await databaseService.SaveMediaImportInfoAsync(importInfo);

        var parsedTags = new List<AppliedTagDTO>();
        foreach (var tag in request.Tags)
        {
            var parsedTag = await tagParser.ParseTag(tag);
            if (parsedTag != null)
            {
                parsedTags.Add(parsedTag.GetDTO());
            }
            else
            {
                await missingTagService.ReportTagAsync(tag, MissingTagTarget.MediaFile, media.Id);
            }
        }

        if (parsedTags.Count > 0)
        {
            await databaseService.AddParsedAppliedTagsToMediaAsync([media.Id], parsedTags);
        }
        else
        {
            logger.LogInformation("No tags parsed for downloaded media file {MediaId}", media.Id);
        }
    }

    private long? GetCachedMediaId(RemoteDownloadRequest request)
    {
        if (recentDownloadUrls.TryGetValue(request.ImageUrl, out var mediaId))
            return mediaId;

        return !string.IsNullOrWhiteSpace(request.CanonicalUrl) &&
               recentDownloadUrls.TryGetValue(request.CanonicalUrl, out mediaId)
            ? mediaId
            : null;
    }

    private void AddCachedUrl(RemoteDownloadRequest request, long mediaId)
    {
        AddCachedUrl(request.ImageUrl, mediaId);
        if (!string.IsNullOrWhiteSpace(request.CanonicalUrl))
            AddCachedUrl(request.CanonicalUrl, mediaId);
    }

    private void AddCachedUrl(string url, long mediaId)
    {
        recentDownloadUrls.Remove(url);
        recentDownloadUrls[url] = mediaId;
        while (recentDownloadUrls.Count > MaximumCachedUrls)
            recentDownloadUrls.Remove(recentDownloadUrls.First().Key);
    }

    private void RemoveCachedUrl(RemoteDownloadRequest request)
    {
        recentDownloadUrls.Remove(request.ImageUrl);
        if (!string.IsNullOrWhiteSpace(request.CanonicalUrl))
            recentDownloadUrls.Remove(request.CanonicalUrl);
    }

    private static string GetImportFileName(RemoteDownloadRequest request)
    {
        var fileName = request.OverrideName;
        if (string.IsNullOrWhiteSpace(fileName) && Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out var imageUri))
            fileName = Path.GetFileName(Uri.UnescapeDataString(imageUri.LocalPath));

        fileName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "remote-download";

        return string.IsNullOrWhiteSpace(Path.GetExtension(fileName)) ? $"{fileName}.jpg" : fileName;
    }
}
