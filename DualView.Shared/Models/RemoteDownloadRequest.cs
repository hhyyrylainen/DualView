using System.Text.Json.Serialization;
using DualView.Shared.Models.Enums;

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
    ///   Optional thumbnail information discovered while scanning a gallery page. Makes displaying this in the GUI
    ///   more efficient by delaying the full size download.
    /// </summary>
    [JsonPropertyName("thumbnail")]
    public RemoteThumbnailInfo? Thumbnail { get; set; }

    /// <summary>
    ///   Gets or sets the detected media type. Plugins should set this when the URL does not contain an extension.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public MediaType? MediaType { get; set; }

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
            Thumbnail = Thumbnail,
            MediaType = MediaType,
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
            Thumbnail = Thumbnail,
            MediaType = MediaType,
        };
    }
}

/// <summary>
///   Pixel rectangle to extract from a remote thumbnail source image.
/// </summary>
public sealed class RemoteThumbnailCrop
{
    /// <summary>
    ///   Gets or sets the horizontal pixel offset in the source image.
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    ///   Gets or sets the vertical pixel offset in the source image.
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    ///   Gets or sets the crop width in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>
    ///   Gets or sets the crop height in pixels.
    /// </summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
///   Describes a thumbnail URL and an optional sprite-sheet crop rectangle.
/// </summary>
public sealed class RemoteThumbnailInfo
{
    /// <summary>
    ///   Gets the URL of the thumbnail image.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; init; }

    /// <summary>
    ///   Gets the optional rectangle to extract from the thumbnail image.
    /// </summary>
    [JsonPropertyName("crop")]
    public RemoteThumbnailCrop? Crop { get; init; }

    /// <summary>
    ///   Creates thumbnail information with an optional source crop rectangle.
    /// </summary>
    /// <param name="url">The thumbnail image URL.</param>
    /// <param name="crop">The optional rectangle to crop from the image.</param>
    public RemoteThumbnailInfo(string url, RemoteThumbnailCrop? crop = null)
    {
        Url = url;
        Crop = crop;
    }
}
