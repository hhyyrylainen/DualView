using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Database.Tests;

public class UploadSectionRoutingTests
{
    [Theory]
    [InlineData("A section name")]
    [InlineData(" A section name ")]
    public async Task UploadThenBrowserImport_KeepsMediaInSelectedSection(string sectionName)
    {
        using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var originalSection = await service.GetOrCreateUploadSectionAsync(sectionName);
        var activeSection = await service.CloneUploadSectionAsync(originalSection.Id);
        var uploadedMedia = new MediaFile("upload.png", "upload-hash");
        var browserMedia = new MediaFile("browser.png", "browser-hash");

        // Upload files without a target name uses the active section, including a clone.
        await service.CreateMediaAsync(uploadedMedia, (string?)null);
        var downloadTarget = await service.GetOrCreateUploadSectionAsync(null);
        await service.CreateMediaInUploadSectionAsync(browserMedia, downloadTarget.Id);

        context.ChangeTracker.Clear();
        var section = await service.GetUploadSectionAsync(activeSection.Id);
        Assert.NotNull(section);
        Assert.Equal([uploadedMedia.Id, browserMedia.Id], section.Items.OrderBy(item => item.Index)
            .Select(item => item.MediaFileId));
        Assert.Empty((await service.GetUploadSectionAsync(originalSection.Id))!.Items);
    }

    [Fact]
    public async Task MissingTarget_RejectsImportBeforeSavingMedia()
    {
        using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var media = new MediaFile("browser.png", "browser-hash");

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMediaInUploadSectionAsync(media, 999));

        Assert.False(await context.MediaFiles.AnyAsync());
    }
}
