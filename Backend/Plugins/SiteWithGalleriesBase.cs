using System.Text.RegularExpressions;
using AngleSharp;
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
public abstract class SiteWithGalleriesBase : IRemoteDownloadPlugin
{
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

    // TODO: regex for gallery thumbnails to allow downloading those for more efficiency of browsing

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
                var result = new PageScanResult();
                result.Content.Add(new RemoteDownloadRequest
                {
                    ImageUrl = download,
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
                // TODO: implement this
                throw new NotImplementedException("general gallery scanning not done yet");

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
        foreach (var tagLink in document.QuerySelectorAll<IHtmlAnchorElement>("a"))
        {
            if (string.IsNullOrEmpty(tagLink.Href))
                continue;

            var match = FullSizeMediaLink.Match(tagLink.Href);
            if (match.Success)
                return tagLink.Href;
        }

        // If no full size link, use fallback
        if (AllowFallbackSmallImage)
        {
            foreach (var tagLink in document.QuerySelectorAll<IHtmlAnchorElement>("a"))
            {
                if (string.IsNullOrEmpty(tagLink.Href))
                    continue;

                var match = MediaPreviewLink.Match(tagLink.Href);
                if (match.Success)
                    return tagLink.Href;
            }
        }

        // Then scan image elements on the page
        foreach (var tagLink in document.QuerySelectorAll<IHtmlImageElement>("img"))
        {
            if (string.IsNullOrEmpty(tagLink.Source))
                continue;

            var match = FullSizeMediaLink.Match(tagLink.Source);
            if (match.Success)
                return tagLink.Source;
        }

        if (AllowFallbackSmallImage)
        {
            foreach (var tagLink in document.QuerySelectorAll<IHtmlImageElement>("img"))
            {
                if (string.IsNullOrEmpty(tagLink.Source))
                    continue;

                var match = MediaPreviewLink.Match(tagLink.Source);
                if (match.Success)
                    return tagLink.Source;
            }
        }

        throw new InvalidOperationException("Cannot find download link");
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
}
