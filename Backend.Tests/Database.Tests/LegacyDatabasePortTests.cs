using Backend.Models;
using Backend.Services;
using DualView.Shared.Models.Enums;

namespace Backend.Tests.Database.Tests;

/// <summary>
///   Relational SQLite equivalents for the legacy database, folder, collection and tag tests.
/// </summary>
public class LegacyDatabasePortTests
{
    [Fact]
    public async Task DatabaseInitialization_CreatesRootAndUncategorizedCollection()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);

        var root = await context.MediaFolders.FindAsync(MediaFolder.RootFolderId);
        var uncategorized = await context.Collections.FindAsync(Collection.UncategorizedCollectionId);

        Assert.NotNull(root);
        Assert.Equal("Root", root.Name);
        Assert.NotNull(uncategorized);
    }

    [Fact]
    public async Task FoldersAndCollections_CanBeCreatedRenamedAndMoved()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);

        var folderId = await service.CreateMediaFolder("  Reference  ", MediaFolder.RootFolderId);
        var otherFolderId = await service.CreateMediaFolder("Other", MediaFolder.RootFolderId);
        var collectionId = await service.CreateCollection("Collection 1", folderId);

        await service.RenameMediaFolder(folderId, "Renamed");
        await service.AddCollectionToFolder(collectionId, otherFolderId);

        var folder = await service.GetMediaFolderAsync(folderId);
        Assert.NotNull(folder);
        var collection = await service.GetCollectionAsync(collectionId);
        Assert.NotNull(collection);
        Assert.Equal("Renamed", folder.Name);
        Assert.Contains(collection.Folders, item => item.Id == otherFolderId);
        Assert.Equal("/Renamed", await service.GetMediaFolderPath(folderId));
    }

    [Fact]
    public async Task Folders_RejectDuplicateNamesWithinOneParent()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);

        await service.CreateMediaFolder("Folder", MediaFolder.RootFolderId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateMediaFolder("folder", MediaFolder.RootFolderId));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCollection("", MediaFolder.RootFolderId));
    }

    [Fact]
    public async Task SoftDeletedMedia_CanBeRestoredAndPurged()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var media = await service.CreateMediaAsync(new MediaFile("image.jpg", "hash-1"),
            Collection.UncategorizedCollectionId);

        await service.DeleteMediaAsync(media.Id);
        Assert.Single(await service.GetDeletedMediaAsync(10));

        await service.RestoreMediaAsync(media.Id);
        Assert.NotNull(await service.GetMediaByIdAsync(media.Id));
        Assert.Empty(await service.GetDeletedMediaAsync(10));
    }

    [Fact]
    public async Task TagParser_ResolvesTagsAliasesAndModifiers()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var tagId = await service.CreateTagAsync("Character", TagCategory.DescribeCharacterObject);
        await service.CreateTagAliasAsync(tagId, "Main Character");
        var modifierId = await service.CreateTagModifierAsync("young");
        var parser = new TagParser(service);

        var alias = await parser.ParseTag("MAIN CHARACTER");
        var modified = await parser.ParseTag("young character");

        Assert.NotNull(alias);
        Assert.Equal(tagId, alias.TagId);
        Assert.NotNull(modified);
        Assert.Equal(tagId, modified.TagId);
        Assert.Contains(modified.Modifiers, modifier => modifier.Id == modifierId);
    }

    [Fact]
    public async Task TagSuggestions_ReturnMatchingNames()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        await service.CreateTagAsync("Red Hair", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Red Eyes", TagCategory.DescribeCharacterObject);
        var parser = new TagParser(service);

        var suggestions = await parser.GetSuggestions("red h");

        Assert.Contains("red hair", suggestions);
    }

    [Fact]
    public async Task UploadSection_ProvidesTwoStepImportFlow()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var section = await service.GetOrCreateUploadSectionAsync("Imported images");
        var media = await service.CreateMediaAsync(new MediaFile("image.jpg", "hash-import"), section.Name);

        var beforeImport = await service.GetUploadSectionAsync(section.Id);
        Assert.NotNull(beforeImport);
        Assert.Contains(beforeImport.Items, item => item.MediaFileId == media.Id);

        await service.ImportUploadSectionAsync(section.Id, null);

        var collection = await service.GetCollectionByNameAsync("Imported images");
        Assert.NotNull(collection);
        Assert.Contains(collection.Items, item => item.MediaFileId == media.Id);
        Assert.Null(await service.GetUploadSectionAsync(section.Id));
    }
}
