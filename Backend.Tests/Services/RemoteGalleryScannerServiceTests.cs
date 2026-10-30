using System.Text.Json;
using Backend.Models;
using Backend.Plugins;
using Backend.Services;
using DualView.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Services;

public class RemoteGalleryScannerServiceTests
{
    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 2)]
    public async Task ResumeGallery_PreservesRemainingQueue(bool failed, bool skipCurrent, int pageCount)
    {
        var pages = Enumerable.Range(1, pageCount)
            .Select(index => new RemoteDownloadRequest { PageUrl = $"https://example.com/{index}", })
            .ToList();
        var gallery = new ScannedCollection("Gallery")
        {
            Id = 1,
            ScanFailed = failed,
            ScanPaused = !failed,
            ScanError = "Broken page",
            CurrentlyScannedUrl = pages.FirstOrDefault()?.HtmlUrl,
            ScannerState = JsonSerializer.Serialize(new { PendingPages = pages, SeenUrls = new[] { "seen", }, }),
        };
        var database = Substitute.For<IDatabaseService>();
        database.GetScannedCollectionsAsync().Returns(Task.FromResult(new List<ScannedCollection>()));
        database.GetScannedCollectionAsync(1).Returns(Task.FromResult<ScannedCollection?>(gallery));
        var appEvents = Substitute.For<IAppEvents>();
        using var provider = new ServiceCollection()
            .AddSingleton(database)
            .AddSingleton(Substitute.For<IEntityUpdateNotifier>())
            .BuildServiceProvider();
        using var service = new RemoteGalleryScannerService(provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IRemoteScanService>(), Substitute.For<IPluginRegistry>(), appEvents,
            Substitute.For<ILogger<RemoteGalleryScannerService>>(), Substitute.For<IRemoteGalleryMediaService>(),
            Substitute.For<IRemoteDownloadProvider>());

        // Stop background processing so the persisted queue can be inspected before the next scan starts.
        await service.StopAsync(TimeSpan.FromSeconds(5));
        await service.ResumeGalleryAsync(1, CancellationToken.None, skipCurrent);

        using var state = JsonDocument.Parse(gallery.ScannerState);
        var remainingUrls = state.RootElement.GetProperty("PendingPages").EnumerateArray()
            .Select(page => page.GetProperty("pageUrl").GetString()).ToList();
        Assert.Equal(pages.Skip(skipCurrent ? 1 : 0).Select(page => page.HtmlUrl), remainingUrls);
        Assert.Equal("seen", state.RootElement.GetProperty("SeenUrls")[0].GetString());
        Assert.False(gallery.ScanFailed);
        Assert.False(gallery.ScanPaused);
        Assert.Null(gallery.ScanError);
        if (skipCurrent)
            Assert.Equal(remainingUrls.FirstOrDefault(), gallery.CurrentlyScannedUrl);
        await database.Received(1).SaveScannedCollectionAsync(gallery);
        appEvents.Received(1).NotifyScannedCollectionCreated(1);
    }
}
