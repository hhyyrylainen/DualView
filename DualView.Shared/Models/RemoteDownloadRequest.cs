using System.Text.Json.Serialization;

namespace DualView.Shared.Models;

/// <summary>
///   Describes media that should be downloaded and placed into an import section. Or to be scanned before finding
///   the content.
/// </summary>
public sealed class RemoteDownloadRequest
{
    /// <summary>
    ///   Gets or sets the image URL to download.
    /// </summary>
    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    ///  Used to deduplicate requests for the same thing but slightly different URL.
    /// </summary>
    [JsonPropertyName("canonicalUrl")]
    public string? CanonicalUrl { get; set; }

    /// <summary>
    ///   Gets or sets the page URL.
    /// </summary>
    [JsonPropertyName("pageUrl")]
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    ///   Gets or sets the page URL to scan for content and subpages.
    /// </summary>
    [JsonPropertyName("linkUrl")]
    public string LinkUrl { get; set; } = string.Empty;

    /// <summary>
    ///   Optional page title that may be known.
    /// </summary>
    [JsonPropertyName("title")]
    public string PageTitle { get; set; } = string.Empty;

    // TODO: add current page HTML if known

    /// <summary>
    ///   Gets or sets the page that referred to the image.
    /// </summary>
    [JsonPropertyName("referrer")]
    public string? Referrer { get; set; }

    /// <summary>
    ///   Gets the tags supplied by the remote scanner.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    ///   Gets or sets the download gallery associated with this media.
    /// </summary>
    [JsonPropertyName("downloadGalleryId")]
    public long? DownloadGalleryId { get; set; }

    /// <summary>
    ///   Gets or sets the import section name to target.
    /// </summary>
    [JsonPropertyName("targetImportSection")]
    public string? TargetImportSection { get; set; }

    /// <summary>
    ///   Gets or sets the name to use instead of deriving one from the URL.
    /// </summary>
    [JsonPropertyName("overrideName")]
    public string? OverrideName { get; set; }

    /// <summary>
    ///   Gets or sets cookies supplied for the target download URL.
    /// </summary>
    [JsonPropertyName("cookies")]
    public Dictionary<string, string> Cookies { get; set; } = new();

    /// <summary>
    ///   Gets or sets browser headers captured when the websocket was opened.
    ///   This is populated by the server and is not accepted from a browser payload.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> ImpersonationHeaders { get; set; } = new();

    public bool Enriched { get; set; }

    /// <summary>
    ///   True if this is a scan request rathen than a direct image download.
    /// </summary>
    public bool IsScanRequest => (!string.IsNullOrEmpty(PageUrl) || !string.IsNullOrEmpty(LinkUrl)) &&
                                 string.IsNullOrEmpty(ImageUrl);

    public string HtmlUrl => !string.IsNullOrEmpty(LinkUrl) ? LinkUrl : PageUrl;

    public RemoteDownloadRequest Clone()
    {
        return new RemoteDownloadRequest
        {
            ImageUrl = ImageUrl,
            Cookies = Cookies,
            ImpersonationHeaders = ImpersonationHeaders,
            PageUrl = PageUrl,
            LinkUrl = LinkUrl,
            PageTitle = PageTitle,
            DownloadGalleryId = DownloadGalleryId,
            TargetImportSection = TargetImportSection,
            OverrideName = OverrideName,
            Enriched = Enriched,
        };
    }

    public RemoteDownloadRequest OverrideForScan(string targetScan)
    {
        return new RemoteDownloadRequest
        {
            Cookies = Cookies,
            ImpersonationHeaders = ImpersonationHeaders,
            PageUrl = targetScan,
            DownloadGalleryId = DownloadGalleryId,
            TargetImportSection = TargetImportSection,
        };
    }
}
