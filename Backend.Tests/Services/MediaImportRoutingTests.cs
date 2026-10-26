using Backend.Models;
using Backend.Services;
using DualView.Shared.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Services;

public class MediaImportRoutingTests
{
    [Fact]
    public async Task ReimportExistingMedia_UsesExplicitSectionWithoutLookingUpItsName()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        var dataFolderService = Substitute.For<IDataFolderService>();
        var targetSection = new UploadSection(" A section name ") { Id = 212 };
        var existingMedia = new MediaFile("browser.png", "hash") { Id = 1012072 };
        databaseService.GetUploadSectionAsync(targetSection.Id).Returns(targetSection);
        databaseService.GetAppSettingsAsync().Returns(new DualViewSettings
        {
            LocalMediaStorageLocation = Path.GetTempPath(),
        });
        databaseService.GetMediaByHashAsync(Arg.Any<string>()).Returns(existingMedia);
        var handler = new MediaImportHandler(Substitute.For<ILogger<MediaImportHandler>>(), databaseService,
            dataFolderService);
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await handler.ImportMedia("browser.png", stream, null, uploadSectionId: targetSection.Id);

        Assert.Same(existingMedia, result);
        await databaseService.Received(1).AddMediaToUploadSectionAsync(existingMedia.Id, targetSection.Id,
            Arg.Any<int>());
        await databaseService.DidNotReceive().GetOrCreateUploadSectionAsync(Arg.Any<string?>());
    }
}
