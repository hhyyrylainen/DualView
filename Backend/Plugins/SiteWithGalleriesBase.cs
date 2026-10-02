using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Backend.Models;
using DualView.Shared.Models;
using DualView.Shared.Utils;
using Microsoft.Extensions.Logging;

namespace Backend.Plugins;

/// <summary>
///   Implements common functionality for websites that have galleries on them for scanning and downloading.
///   It is assumed the structure is: somehow getting to a gallery -> gallery has either content or other page links
///   -> gallery page links eventually have content page links -> content pages have links to the full size images.
///   Note: this doesn't use the top level gallery metaphor as browsing over the galleries is not supported.
/// </summary>
public abstract class SiteWithGalleriesBase : IRemoteDownloadPlugin, IRemoteGalleryNameFilter
{
    private static readonly Regex GrabCssUrl = new(@"url\([""']?(.*?)[""']?\)", RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    protected readonly ILogger Logger;

    protected SiteWithGalleriesBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract string Name { get; }

    public abstract string Version { get; }

    /// <summary>
    ///   Regex that matches only full-size media links.
    /// </summary>
    protected abstract Regex FullSizeMediaLink { get; }

    /// <summary>
    ///   Regex that matches preview media links. (may also match full-size media links)
    /// </summary>
    protected abstract Regex MediaPreviewLink { get; }

    /// <summary>
    ///   Matches a page of a gallery on this site. Should have capture groups for the gallery ID and page number.
    /// </summary>
    protected abstract Regex GalleryPageLink { get; }

    /// <summary>
    ///   Needs to match content pages on this site.
    ///   Each gallery page should have links to content pages and further gallery pages.
    /// </summary>
    protected abstract Regex ContentPageRegex { get; }

    /// <summary>
    ///   Base URL for this site. Should end with a slash.
    /// </summary>
    protected abstract string SiteBaseUrl { get; }

    public bool AllowFallbackSmallImage { get; protected set; }

    public virtual Task OnStart(IPluginRegistry registry)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnStop()
    {
        return Task.CompletedTask;
    }

    public Task<UrlInformation> InspectWebsiteRequest(RemoteDownloadRequest request,
        IRemoteDownloadProvider downloadProvider,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.ImageUrl))
        {
            if (FullSizeMediaLink.IsMatch(request.ImageUrl))
            {
                return Task.FromResult(UrlInformation.ContentLink);
            }
        }

        var htmlUrl = request.HtmlUrl;
        if (!string.IsNullOrEmpty(htmlUrl))
        {
            if (ContentPageRegex.IsMatch(htmlUrl))
                return Task.FromResult(UrlInformation.ContentPage);

            if (GalleryPageLink.IsMatch(htmlUrl))
                return Task.FromResult(UrlInformation.GalleryPage);
        }

        if (!string.IsNullOrEmpty(request.ImageUrl))
        {
            if (MediaPreviewLink.IsMatch(request.ImageUrl))
            {
                return Task.FromResult(UrlInformation.ContentLink);
            }
        }

        return Task.FromResult(UrlInformation.Unknown);
    }

    /// <summary>
    ///   Converts a URL to a canonical identity for deduplication. The returned value may be synthetic and must not
    ///   be used to download or scan the page; keep using the original URL for those operations.
    /// </summary>
    /// <param name="url">URL to convert.</param>
    /// <returns>A canonical URL for comparing URL identity.</returns>
    public virtual string ConvertToCanonicalUrl(string url)
    {
        // Based on the URL definitions, we can only make canonical gallery pages
        var match = GalleryPageLink.Match(url);
        if (match.Success)
        {
            return SiteBaseUrl + "gallerySynth/" + match.Groups[1].Value + "&page=" + match.Groups[2].Value;
        }

        return url;
    }

    public virtual Task<RemoteDownloadRequest?> EnrichDownloadAsync(RemoteDownloadRequest request,
        IRemoteDownloadProvider downloadProvider,
        CancellationToken cancellationToken)
    {
        // TODO: this could try to get the gallery from the image request to enrich the download info
        return Task.FromResult<RemoteDownloadRequest?>(null);
    }

    public virtual async Task<PageScanResult> ScanPageAsync(RemoteDownloadRequest request,
        IRemoteDownloadProvider downloadProvider, CancellationToken cancellationToken)
    {
        // Determine if it is a content page or something else
        var type = await InspectWebsiteRequest(request, downloadProvider, cancellationToken);

        var content = await downloadProvider.DownloadHtmlAsync(request, cancellationToken);

        var context = BrowsingContext.New(Configuration.Default);
        var document =
            await context.OpenAsync(r => r.Content(content).Address(request.HtmlUrl), cancel: cancellationToken);

        switch (type)
        {
            case UrlInformation.ContentPage:
            {
                var tags = await GetTagsForContentPage(document, request);
                var download = GetDownloadUrlFromPage(document);
                var downloadUrls = GetDownloadUrlsFromPage(document);
                var result = new PageScanResult();
                result.Content.Add(new RemoteDownloadRequest
                {
                    ImageUrl = download,
                    Thumbnail = downloadUrls.Count == 1 ? request.Thumbnail : null,
                    Referrer = request.HtmlUrl,
                    PageUrl = request.HtmlUrl,
                    CanonicalUrl = ConvertToCanonicalUrl(download),
                    ImpersonationHeaders = request.ImpersonationHeaders.CloneShallow(),
                    TargetImportSection = request.TargetImportSection,
                    Cookies = request.Cookies.CloneShallow(),
                    Enriched = true,
                    Tags = tags,
                    OverrideName = GetDownloadOverrideName(document, request),
                });

                Logger.LogInformation("Found website content: {Url}, referrer: {Referrer}", download, request.HtmlUrl);

                return result;
            }

            case UrlInformation.GalleryPage:
            case UrlInformation.GalleryPageContent:
            {
                var tags = await GetTagsForGalleryPage(document, request);

                var result = new PageScanResult
                {
                    GalleryInfo = new DualView.Shared.Models.DTO.DownloadGalleryDTO(request.HtmlUrl)
                    {
                        GalleryName = FilterGalleryName(document.Title),
                        TagsString = tags.Count == 0 ? null : string.Join(", ", tags),
                    },
                };

                foreach (var link in document.QuerySelectorAll<IHtmlAnchorElement>("a"))
                {
                    if (string.IsNullOrWhiteSpace(link.Href))
                        continue;

                    if (GalleryPageLink.IsMatch(link.Href) || ContentPageRegex.IsMatch(link.Href))
                    {
                        var subpage = request.OverrideForScan(link.Href);
                        subpage.Referrer = request.HtmlUrl;

                        // Gallery pages link to the items, and those can be represented by thumbnails. Content pages
                        // can also link to other content pages, so we basically always want to try to detect a
                        // thumbnail for the link here.
                        subpage.Thumbnail = GetThumbnailForContentPage(link);

                        result.Subpages.Add(subpage);
                        continue;
                    }

                    if (FullSizeMediaLink.IsMatch(link.Href))
                    {
                        result.Content.Add(new RemoteDownloadRequest
                        {
                            ImageUrl = link.Href,
                            Thumbnail = GetThumbnailForContentPage(link),
                            Referrer = request.HtmlUrl,
                            CanonicalUrl = ConvertToCanonicalUrl(link.Href),
                            ImpersonationHeaders = request.ImpersonationHeaders.CloneShallow(),
                            Cookies = request.Cookies.CloneShallow(),
                        });
                    }
                }

                return result;
            }

            case UrlInformation.ContentLink:
                throw new InvalidOperationException("Cannot scan a content link");
            default:
                throw new InvalidOperationException("Unknown page type to scan");
        }
    }

    /// <summary>
    ///   Allows detecting the proper name for a download.
    /// </summary>
    /// <param name="document">Document of the HTML page</param>
    /// <param name="contentHtmlRequest">The URL of the page that refers to the media</param>
    /// <returns>Override name or null</returns>
    protected virtual string? GetDownloadOverrideName(IDocument document, RemoteDownloadRequest contentHtmlRequest)
    {
        return null;
    }

    protected virtual string GetDownloadUrlFromPage(IDocument document)
    {
        return GetDownloadUrlsFromPage(document).FirstOrDefault() ??
               throw new InvalidOperationException("Cannot find download link");
    }

    /// <summary>
    ///   Gets the thumbnail associated with a content page link in a gallery listing.
    /// </summary>
    /// <param name="contentPageLink">The gallery link that points to a content page.</param>
    /// <returns>The thumbnail URL, or null if the link has no thumbnail.</returns>
    protected virtual string? GetThumbnailUrlForContentPage(IHtmlAnchorElement contentPageLink)
    {
        var thumbnailUrl = contentPageLink.QuerySelector<IHtmlImageElement>("img")?.Source;

        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return thumbnailUrl;
        }

        // More advanced case. Find any child element with a background set to an URL that looks like media
        foreach (var descendant in contentPageLink.Descendants<IHtmlElement>())
        {
            var style = descendant.GetAttribute("style");
            if (string.IsNullOrWhiteSpace(style) || !style.Contains("url", StringComparison.OrdinalIgnoreCase))
                continue;

            var declaration = new CssParser().ParseDeclaration(style);
            if (declaration == null)
                continue;

            var match = GrabCssUrl.Match(declaration.GetPropertyValue("background-image"));
            if (match.Success)
            {
                var url = match.Groups[1].Value;

                if (MediaPreviewLink.IsMatch(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///   Gets the thumbnail URL and optional sprite-sheet crop for a content page link.
    /// </summary>
    /// <param name="contentPageLink">The gallery link that points to a content page.</param>
    /// <returns>The thumbnail information, or null if the link has no thumbnail.</returns>
    protected virtual RemoteThumbnailInfo? GetThumbnailForContentPage(IHtmlAnchorElement contentPageLink)
    {
        var thumbnailUrl = GetThumbnailUrlForContentPage(contentPageLink);
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
            return null;

        // Postprocessing in case background uses cropping
        foreach (var descendant in contentPageLink.Descendants<IHtmlElement>())
        {
            var style = descendant.GetAttribute("style");
            if (string.IsNullOrWhiteSpace(style))
                continue;

            var declaration = new CssParser().ParseDeclaration(style);
            if (declaration == null)
                continue;

            var backgroundImage = declaration.GetPropertyValue("background-image") ?? string.Empty;
            var backgroundMatch = GrabCssUrl.Match(backgroundImage);
            if (!backgroundMatch.Success || backgroundMatch.Groups[1].Value != thumbnailUrl)
                continue;

            if (TryGetPixelValue(declaration.GetPropertyValue("width"), out var width) &&
                TryGetPixelValue(declaration.GetPropertyValue("height"), out var height) &&
                TryGetBackgroundPosition(declaration.GetPropertyValue("background-position-x"),
                    declaration.GetPropertyValue("background-position-y"), out var x, out var y))
            {
                return new RemoteThumbnailInfo(thumbnailUrl, new RemoteThumbnailCrop
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                });
            }

            break;
        }

        return new RemoteThumbnailInfo(thumbnailUrl);
    }

    private static bool TryGetPixelValue(string? value, out int pixels)
    {
        pixels = 0;
        value ??= string.Empty;
        value = value.Trim();
        if (value == "0")
            return true;

        return value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(value.AsSpan(0, value.Length - 2), out pixels) && pixels > 0;
    }

    private static bool TryGetBackgroundPosition(string? horizontalPosition, string? verticalPosition,
        out int x, out int y)
    {
        x = 0;
        y = 0;
        return TryParsePosition(horizontalPosition, true, out x) &&
               TryParsePosition(verticalPosition, false, out y);
    }

    private static bool TryParsePosition(string? value, bool horizontal, out int position)
    {
        position = 0;
        value ??= string.Empty;
        value = value.Trim();
        if ((horizontal && value.Equals("left", StringComparison.OrdinalIgnoreCase)) ||
            (!horizontal && value.Equals("top", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (int.TryParse(value, out var unitlessOffset) && unitlessOffset == 0)
            return true;

        if (!value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(value.AsSpan(0, value.Length - 2), out var offset))
        {
            return false;
        }

        position = -offset;
        return position >= 0;
    }

    /// <summary>
    ///   Finds the media URLs on a content page using the same priority as <see cref="GetDownloadUrlFromPage"/>.
    /// </summary>
    /// <param name="document">The parsed content page.</param>
    /// <returns>Distinct matching media URLs in document order, or an empty list if no URL matches.</returns>
    protected virtual List<string> GetDownloadUrlsFromPage(IDocument document)
    {
        var anchorUrls = document.QuerySelectorAll<IHtmlAnchorElement>("a").Select(link => link.Href);
        var imageUrls = document.QuerySelectorAll<IHtmlImageElement>("img").Select(image => image.Source);
        var validAnchorUrls = anchorUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
        var validImageUrls = imageUrls.OfType<string>()
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        var fullSizeAnchorUrls = validAnchorUrls
            .Where(url => FullSizeMediaLink.IsMatch(url))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (fullSizeAnchorUrls.Count > 0)
            return fullSizeAnchorUrls;

        if (AllowFallbackSmallImage)
        {
            var previewAnchorUrls = validAnchorUrls
                .Where(url => MediaPreviewLink.IsMatch(url))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (previewAnchorUrls.Count > 0)
                return previewAnchorUrls;
        }

        var fullSizeImageUrls = validImageUrls
            .Where(url => FullSizeMediaLink.IsMatch(url))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (fullSizeImageUrls.Count > 0)
            return fullSizeImageUrls;

        if (AllowFallbackSmallImage)
        {
            var previewImageUrls = validImageUrls
                .Where(url => MediaPreviewLink.IsMatch(url))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (previewImageUrls.Count > 0)
                return previewImageUrls;
        }

        return new List<string>();
    }

    /// <summary>
    ///   Allows getting tags for a content page. By default, doesn't find anything as most gallery sites don't have
    ///   per-image tags.
    /// </summary>
    /// <param name="document">Parsed HTML of the content page</param>
    /// <param name="contentHtmlRequest">
    ///   Request done for the page, in case this wants to do something with it like fetching extra pages
    /// </param>
    /// <returns>Tags list or an empty list</returns>
    protected virtual Task<List<string>> GetTagsForContentPage(IDocument document,
        RemoteDownloadRequest contentHtmlRequest)
    {
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    ///   Allows getting tags for a gallery page. By default, doesn't find any tags.
    /// </summary>
    /// <param name="document">Parsed HTML of the gallery page.</param>
    /// <param name="galleryPageRequest">Request done for the gallery page.</param>
    /// <returns>Tags list or an empty list.</returns>
    protected virtual Task<List<string>> GetTagsForGalleryPage(IDocument document,
        RemoteDownloadRequest galleryPageRequest)
    {
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    ///   Filters a gallery name extracted from a gallery page. By default, leaves the name unchanged.
    /// </summary>
    /// <param name="galleryName">The name extracted from the page.</param>
    /// <returns>The filtered gallery name.</returns>
    public virtual string? FilterGalleryName(string? galleryName)
    {
        return galleryName;
    }
}
