using Backend.Models;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;

namespace Backend.Plugins;

public interface IRemoteDownloadPlugin : IPlugin
{
    /// <summary>
    ///   Checks if this plugin can handle the given request and returns some info.
    /// </summary>
    /// <param name="request">Request to check</param>
    /// <param name="downloadProvider">This is used to fetch remote content</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>An enum describing what this plugin knows about the website</returns>
    public Task<UrlInformation> InspectWebsiteRequest(RemoteDownloadRequest request,
        IRemoteDownloadProvider downloadProvider, CancellationToken cancellationToken);

    /// <summary>
    ///   For supported websites, this will convert a URL to a canonical URL. This ensures that the same URL is not
    ///   downloaded multiple times.
    /// </summary>
    /// <param name="url">
    ///   Url to convert to canonical (what this most often does is strip unnecessary query parameters)
    /// </param>
    /// <returns>Canonical representation of the URL</returns>
    public string ConvertToCanonicalUrl(string url);

    /// <summary>
    ///   Enriches the download request with additional information. Only valid if <see cref="InspectWebsiteRequest"/>
    ///   returns not unknown status. This will mostly look for tags to apply, but also does switch to a full size URL
    ///   on some websites that have preview URLs.
    /// </summary>
    /// <param name="request">Request to enrich</param>
    /// <param name="downloadProvider">This is used to fetch remote content</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Enriched request (should not modify the original) or null if there's nothing to add</returns>
    /// <exception cref="NotSupportedException">If passed an URL unknown to this plugin</exception>
    public Task<RemoteDownloadRequest?> EnrichDownloadAsync(RemoteDownloadRequest request,
        IRemoteDownloadProvider downloadProvider, CancellationToken cancellationToken);

    /// <summary>
    ///   Scans a page for subpages and content.
    /// </summary>
    /// <param name="request">Page to scan</param>
    /// <param name="downloadProvider">This is used to fetch remote content</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>The scan results. Will throw on error.</returns>
    public Task<PageScanResult> ScanPageAsync(RemoteDownloadRequest request, IRemoteDownloadProvider downloadProvider,
        CancellationToken cancellationToken);
}

public interface IRemoteDownloadProvider
{
    public Task<string> DownloadHtmlAsync(RemoteDownloadRequest request, CancellationToken cancellationToken);
}

/// <summary>
///   Result of scanning a page for subpages and content.
/// </summary>
public class PageScanResult
{
    /// <summary>
    ///   Subpages found on the page. Either gallery or image pages.
    /// </summary>
    public List<RemoteDownloadRequest> Subpages { get; set; } = new();

    /// <summary>
    ///   Content found on the page for direct download.
    /// </summary>
    public List<RemoteDownloadRequest> Content { get; set; } = new();

    /// <summary>
    ///   If gallery info is available, this will contain the gallery info as scanned from the page.
    /// </summary>
    public DownloadGalleryDTO? GalleryInfo { get; set; }
}
