using System.Net;
using System.Threading.Channels;
using AsyncKeyedLock;
using Backend.Models;
using Backend.Plugins;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Coordinates remote scan enrichment and stores remote processing events.
/// </summary>
public sealed class RemoteScanService : IRemoteScanService, IRemoteDownloadProvider
{
    private const int MaximumScanAttempts = 5;

    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly ILogger<RemoteScanService> logger;
    private readonly IPluginRegistry pluginRegistry;

    private readonly SemaphoreSlim eventLock = new(1, 1);
    private readonly List<RemoteScanEvent> events = new();

    private readonly HttpClient httpClient;

    /// <summary>
    ///   Used to limit concurrent scans of the same domain.
    /// </summary>
    private readonly AsyncKeyedLocker<string> domainScanLocks = new();

    private readonly Channel<ScanWorkItem> scanQueue = Channel.CreateUnbounded<ScanWorkItem>();

    private readonly Task scanWorkerTask;
    private readonly CancellationTokenSource shutdownCancellationSource = new();

    public RemoteScanService(IServiceScopeFactory serviceScopeFactory, ILogger<RemoteScanService> logger,
        IPluginRegistry pluginRegistry)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.logger = logger;
        this.pluginRegistry = pluginRegistry;

        var httpHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingDelay = TimeSpan.FromMinutes(2),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false,
        };

        // TODO: determine if this should also disable SSL checking like downloads
        // httpHandler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        httpClient = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromMinutes(1),
        };

        scanWorkerTask = Task.Run(ProcessScanQueueAsync);
    }

    public async Task<RemoteDownloadRequest> EnrichDownloadAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken)
    {
        var plugins = pluginRegistry.GetRemoteDownloadPlugins();
        bool enriched = false;

        foreach (var remoteDownloadPlugin in plugins)
        {
            var pluginSupport = await remoteDownloadPlugin.InspectWebsiteRequest(request, this, cancellationToken);

            if (pluginSupport == UrlInformation.Unknown)
                continue;

            var updatedRequest = await remoteDownloadPlugin.EnrichDownloadAsync(request, this, cancellationToken);

            if (pluginSupport == UrlInformation.ContentLink)
            {
                // This is a direct image link!
                var temporary = updatedRequest ?? request;

                // Stop scan attempts
                temporary.LinkUrl = "";
                temporary.PageUrl = "";

                return temporary;
            }

            if (updatedRequest != null)
            {
                request = updatedRequest;
                enriched = true;
                break;
            }
        }

        if (enriched)
        {
            using var scope = serviceScopeFactory.CreateScope();
            await RecordEventInternalAsync(new RemoteScanEvent
            {
                EventType = "download-enrichment-done",
                ImageUrl = request.ImageUrl,

                // TODO: split remote scan events into "internal" and user-readable ones, and set a duration after which
                // they get cleared
            }, cancellationToken);
        }

        return request;
    }

    public void OnShutdown()
    {
        scanQueue.Writer.TryComplete();
        shutdownCancellationSource.Cancel();
        scanWorkerTask.GetAwaiter().GetResult();
    }

    public async Task<UrlInformation> InspectUrlAsync(RemoteDownloadRequest request,
        CancellationToken cancellationToken)
    {
        var plugins = pluginRegistry.GetRemoteDownloadPlugins();

        foreach (var plugin in plugins)
        {
            var result = await plugin.InspectWebsiteRequest(request, this, cancellationToken);
            if (result != UrlInformation.Unknown)
            {
                // First plugin that knows it returns its info
                return result;
            }
        }

        // No plugin knows this, but we can check if the URL ends with a media extension, and if so, we can assume
        // it to be a direct download
        var parsed = new Uri(string.IsNullOrEmpty(request.HtmlUrl) ? request.ImageUrl : request.HtmlUrl);
        var extension = Path.GetExtension(Uri.UnescapeDataString(parsed.Segments.LastOrDefault() ?? ""));

        logger.LogInformation("Checking URL extension: {Extension}", extension);

        if (!string.IsNullOrEmpty(extension) && extension.StartsWith("."))
        {
            try
            {
                _ = MediaTypeExtensions.TypeFromExtension(extension);
                return UrlInformation.ContentLink;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Unsupported content extension: {Extension}", extension);
            }
        }

        logger.LogInformation("URL we can do nothing about: {Url}", request.HtmlUrl);
        return UrlInformation.Unknown;
    }

    public async Task<PageScanResult> ScanContentPage(RemoteDownloadRequest pageRequest, bool highPriority,
        CancellationToken cancellation)
    {
        var workItem = new ScanWorkItem(pageRequest, cancellation);
        await scanQueue.Writer.WriteAsync(workItem, cancellation);
        return await workItem.Completion.Task.WaitAsync(cancellation);
    }

    public string GetDomainForRequestScan(RemoteDownloadRequest request)
    {
        var target = request.HtmlUrl;
        if (string.IsNullOrEmpty(target))
            target = request.ImageUrl;

        return GetDomainForRequestScan(target);
    }

    public string GetDomainForRequestScan(string url)
    {
        return new Uri(url).Host;
    }

    public async Task RecordEventAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken)
    {
        await RecordEventInternalAsync(scanEvent, cancellationToken);
    }

    public async Task<string> DownloadHtmlAsync(RemoteDownloadRequest request, CancellationToken cancellationToken)
    {
        using var scanLock =
            await domainScanLocks.LockAsync(GetDomainForRequestScan(request.HtmlUrl), cancellationToken);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.HtmlUrl);

        // Set impersonation headers if provided
        if (request.ImpersonationHeaders is { Count: > 0 })
        {
            new BrowserImpersonationHeaders(request.ImpersonationHeaders).ConfigureHttpRequest(httpRequest, true);
        }
        else
        {
            // Set a default user agent
            httpRequest.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (X11; Linux x86_64; rv:154.0) Gecko/20100101 Firefox/153.0");
        }

        if (!string.IsNullOrWhiteSpace(request.Referrer) && Uri.TryCreate(request.Referrer, UriKind.Absolute,
                out var referrer))
        {
            httpRequest.Headers.Referrer = referrer;
        }

        httpRequest.Headers.Host = new Uri(request.HtmlUrl).Host;

        if (request.Cookies.Count > 0)
        {
            var cookieHeader = string.Join("; ", request.Cookies.Select(cookie =>
                $"{cookie.Key}={cookie.Value}"));
            httpRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        // Read full content as HTML pages are assumed to be quite small
        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // TODO: add filter parameter for user readable or all, and whether to clear user-readable events or not. These features will allow implementing a GUI.
    public async Task<IReadOnlyList<RemoteScanEvent>> GetEventsAsync(CancellationToken cancellationToken)
    {
        await eventLock.WaitAsync(cancellationToken);
        try
        {
            return events.ToList();
        }
        finally
        {
            eventLock.Release();
        }
    }

    private async Task RecordEventInternalAsync(RemoteScanEvent scanEvent, CancellationToken cancellationToken)
    {
        await eventLock.WaitAsync(cancellationToken);
        try
        {
            events.Add(scanEvent);
            logger.LogInformation("Remote scan event {EventType} for {ImageUrl}: {Detail}",
                scanEvent.EventType, scanEvent.ImageUrl, scanEvent.Detail);
        }
        finally
        {
            eventLock.Release();
        }
    }

    private async Task ProcessScanQueueAsync()
    {
        while (await scanQueue.Reader.WaitToReadAsync())
        {
            while (scanQueue.Reader.TryRead(out var workItem))
            {
                try
                {
                    var result = await ScanContentPageWithRetryAsync(workItem.PageRequest, workItem.Cancellation);
                    workItem.Completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    workItem.Completion.TrySetException(ex);
                }

                if (shutdownCancellationSource.IsCancellationRequested)
                {
                    while (scanQueue.Reader.TryRead(out var queuedWorkItem))
                        queuedWorkItem.Completion.TrySetCanceled();

                    return;
                }
            }
        }
    }

    private async Task<PageScanResult> ScanContentPageWithRetryAsync(RemoteDownloadRequest pageRequest,
        CancellationToken cancellation)
    {
        for (var attempt = 1; attempt <= MaximumScanAttempts; ++attempt)
        {
            try
            {
                var plugins = pluginRegistry.GetRemoteDownloadPlugins();
                foreach (var plugin in plugins)
                {
                    var result = await plugin.InspectWebsiteRequest(pageRequest, this, cancellation);
                    if ((result & UrlInformation.GalleryPageContent) != 0)
                        return await plugin.ScanPageAsync(pageRequest, this, cancellation);
                }

                throw new InvalidOperationException("No plugin found that accepted the scan request");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordEventAsync(new RemoteScanEvent
                {
                    EventType = attempt == MaximumScanAttempts ? "scan-failed" : "scan-retry",
                    ImageUrl = pageRequest.HtmlUrl,
                    Detail = ex.Message,
                    Attempt = attempt,
                }, cancellation);

                if (attempt == MaximumScanAttempts)
                    throw;

                await Task.Delay(TimeSpan.FromSeconds(3), cancellation);
            }
        }

        throw new InvalidOperationException("Scan retry loop ended unexpectedly");
    }

    private sealed class ScanWorkItem
    {
        public RemoteDownloadRequest PageRequest { get; }
        public CancellationToken Cancellation { get; }

        public TaskCompletionSource<PageScanResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScanWorkItem(RemoteDownloadRequest pageRequest, CancellationToken cancellation)
        {
            PageRequest = pageRequest;
            Cancellation = cancellation;
        }
    }
}
