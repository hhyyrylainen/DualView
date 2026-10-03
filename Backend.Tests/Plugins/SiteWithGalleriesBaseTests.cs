using System.Text.RegularExpressions;
using Backend.Plugins;
using DualView.Shared.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Plugins;

public class SiteWithGalleriesBaseTests
{
    private const string GalleryPageUrl = "https://example.com/gallery/test";
    private const string ContentPageUrl = "https://example.com/item/asset?page=11";

    public static IEnumerable<object[]> DetachedGalleryPageCases =>
    [
        [new[] { 1, 2, 3, }, 2, 1, new[] { 1, 2, 3, },],
        [new[] { 1, 2, 3, 5, }, 2, 1, new[] { 1, 2, 3, },],
        [new[] { 1, 3, 4, 11, }, 2, 1, new[] { 1, 3, 4, },],
        [new[] { 1, 3, 4, 5, }, 2, 1, new[] { 1, 3, 4, 5, },],
        [new[] { 1, 2, 3, 5, 6, }, 2, 1, new[] { 1, 2, 3, 5, 6, },],
        [new[] { 1, 3, 4, 11, 12, }, 2, 2, new[] { 1, 3, 4, },],
        [new[] { 1, 2, 2, 3, 5, 5, }, 2, 1, new[] { 1, 2, 2, 3, },],
        [new[] { 1, 2, 4, 7, }, 2, 1, new[] { 1, 2, 4, },],
        [new[] { 1, 2, 3, 5, }, 2, 0, new[] { 1, 2, 3, 5, },],
    ];

    [Theory]
    [MemberData(nameof(DetachedGalleryPageCases))]
    public async Task ScanPageAsync_SkipsOnlyDetachedTrailingGalleryPages(int[] pageNumbers, int currentPageNumber,
        int ignoreDetachedLastNGalleryLinks, int[] expectedPageNumbers)
    {
        var html = string.Join("", pageNumbers.Select(pageNumber =>
                       $"<a href=\"{GalleryPageUrl}?page={pageNumber}\">{pageNumber}</a>")) +
                   $"<a href=\"{ContentPageUrl}\">content</a>";
        var plugin = new TestGalleryPlugin(Substitute.For<ILogger>(),
            ignoreDetachedLastNGalleryLinks);
        var downloadProvider = Substitute.For<IRemoteDownloadProvider>();
        downloadProvider.DownloadHtmlAsync(Arg.Any<RemoteDownloadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(html));

        var result = await plugin.ScanPageAsync(new RemoteDownloadRequest
        {
            PageUrl = $"{GalleryPageUrl}?page={currentPageNumber}",
        }, downloadProvider, CancellationToken.None);

        var expectedUrls = expectedPageNumbers
            .Select(pageNumber => $"{GalleryPageUrl}?page={pageNumber}")
            .Append(ContentPageUrl);
        Assert.Equal(expectedUrls, result.Subpages.Select(subpage => subpage.HtmlUrl));
    }

    private sealed class TestGalleryPlugin(ILogger logger, int ignoreDetachedLastNGalleryLinks)
        : SiteWithGalleriesBase(logger)
    {
        public override string Name => "Test Gallery";

        public override string Version => "1";

        public override int DefaultFirstPageNumber => 1;

        public override int IgnoreDetachedLastNGalleryLinks => ignoreDetachedLastNGalleryLinks;

        protected override Regex FullSizeMediaLink { get; } = new(@"^https://example\.com/media/full/");

        protected override Regex MediaPreviewLink { get; } = new(@"^https://example\.com/media/preview/");

        protected override Regex GalleryPageLink { get; } =
            new(@"^https://example\.com/gallery/([^?]+)(?:\?page=(\d+))?$");

        protected override Regex ContentPageRegex { get; } = new(@"^https://example\.com/item/");

        protected override string SiteBaseUrl => "https://example.com/";
    }
}
