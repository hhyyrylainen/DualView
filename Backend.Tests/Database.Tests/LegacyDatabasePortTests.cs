using Backend.Database;
using Backend.Models;
using Backend.Services;
using DualView.Shared.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
    public async Task UploadTargetNameSearch_PrioritizesCasePrefixPositionAndLength()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);

        await service.CreateCollection("Cat", MediaFolder.RootFolderId);
        await service.CreateCollection("Cathedral", MediaFolder.RootFolderId);
        await service.CreateCollection("catwalk", MediaFolder.RootFolderId);
        await service.CreateCollection("The Cat", MediaFolder.RootFolderId);
        await service.CreateCollection("concatenate", MediaFolder.RootFolderId);

        var names = await service.SearchUploadTargetNamesAsync("Cat");

        Assert.Equal(["Cat", "Cathedral", "catwalk", "The Cat", "Uncategorized", "concatenate"], names);
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
    public async Task Folders_CanBeAddedAndRemovedFromFolders()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var firstParentId = await service.CreateMediaFolder("First parent", MediaFolder.RootFolderId);
        var secondParentId = await service.CreateMediaFolder("Second parent", MediaFolder.RootFolderId);
        var childId = await service.CreateMediaFolder("Child", MediaFolder.RootFolderId);

        await service.AddFolderToFolder(childId, firstParentId);
        await service.AddFolderToFolder(childId, secondParentId);

        var firstParentChildren = await service.GetMediaFoldersAsync(firstParentId);
        var secondParentChildren = await service.GetMediaFoldersAsync(secondParentId);
        Assert.Contains(firstParentChildren, folder => folder.Id == childId);
        Assert.Contains(secondParentChildren, folder => folder.Id == childId);

        await service.RemoveFolderFromFolder(childId, firstParentId);
        Assert.DoesNotContain(await service.GetMediaFoldersAsync(firstParentId), folder => folder.Id == childId);
        Assert.Contains(await service.GetMediaFoldersAsync(secondParentId), folder => folder.Id == childId);

        await service.RemoveFolderFromFolder(childId, secondParentId);
        Assert.Contains(await service.GetMediaFoldersAsync(MediaFolder.RootFolderId), folder => folder.Id == childId);
    }

    [Fact]
    public async Task FolderContents_KeepRootAndNestedItemsSeparate()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var folderId = await service.CreateMediaFolder("Nested", MediaFolder.RootFolderId);
        var childId = await service.CreateMediaFolder("Child", folderId);
        var collectionId = await service.CreateCollection("Nested collection", folderId);

        var rootFolders = await service.GetMediaFoldersAsync(MediaFolder.RootFolderId);
        var nestedFolders = await service.GetMediaFoldersAsync(folderId);
        var rootCollections = await service.GetCollectionsInFolderAsync(MediaFolder.RootFolderId);
        var nestedCollections = await service.GetCollectionsInFolderAsync(folderId);

        Assert.Contains(rootFolders, folder => folder.Id == folderId);
        Assert.DoesNotContain(rootFolders, folder => folder.Id == childId);
        Assert.Contains(nestedFolders, folder => folder.Id == childId);
        Assert.DoesNotContain(rootCollections, collection => collection.Id == collectionId);
        Assert.Contains(nestedCollections, collection => collection.Id == collectionId);
        Assert.Equal(childId, (await service.GetMediaFolderFromPathAsync("Root/Nested/Child"))!.Id);
    }

    [Fact]
    public async Task RemovingLastCollectionParent_ReturnsCollectionToRoot()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var folderId = await service.CreateMediaFolder("Collection folder", MediaFolder.RootFolderId);
        var collectionId = await service.CreateCollection("Movable collection", folderId);

        await service.RemoveCollectionFromFolder(collectionId, folderId);

        Assert.Contains(await service.GetCollectionsInFolderAsync(MediaFolder.RootFolderId),
            collection => collection.Id == collectionId);
        Assert.DoesNotContain(await service.GetCollectionsInFolderAsync(folderId),
            collection => collection.Id == collectionId);
    }

    [Fact]
    public async Task Folders_RejectConflictingSiblingWhenAddedAndRenamed()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var parentId = await service.CreateMediaFolder("Parent", MediaFolder.RootFolderId);
        var movingId = await service.CreateMediaFolder("Moving", MediaFolder.RootFolderId);
        var conflictId = await service.CreateMediaFolder("Moving", parentId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFolderToFolder(movingId, parentId));
        Assert.DoesNotContain(await service.GetMediaFoldersAsync(parentId), folder => folder.Id == movingId);
        Assert.Contains(await service.GetMediaFoldersAsync(parentId), folder => folder.Id == conflictId);

        var otherId = await service.CreateMediaFolder("Other", parentId);
        await Assert.ThrowsAsync<Exception>(() => service.RenameMediaFolder(otherId, "moving"));
        var other = await service.GetMediaFolderAsync(otherId);
        Assert.Equal("Other", other!.Name);
    }

    [Fact]
    public async Task RenamingFolderAndCollection_UpdatesLowercaseName()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var folderId = await service.CreateMediaFolder("Original folder", MediaFolder.RootFolderId);
        var collectionId = await service.CreateCollection("Original collection", folderId);

        await service.RenameMediaFolder(folderId, "Renamed Folder");
        await service.RenameCollection(collectionId, "Renamed Collection");

        var folder = await context.MediaFolders.FindAsync(folderId);
        var collection = await context.Collections.FindAsync(collectionId);
        Assert.Equal("Renamed Folder", folder!.Name);
        Assert.Equal("renamed folder", folder.NameLowerCase);
        Assert.Equal("Renamed Collection", collection!.Name);
        Assert.Equal("renamed collection", collection.NameLowerCase);
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
    public async Task TagSuggestions_PutExactTagNameBeforeLongerMatchingNames()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        await service.CreateTagAsync("table", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("red table", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("sturdy table", TagCategory.DescribeCharacterObject);
        var parser = new TagParser(service);

        var suggestions = await parser.GetSuggestions("tabl");

        Assert.Equal("table", suggestions[0]);
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

    [Fact]
    public async Task ImportUploadSection_UpdatesLastImported()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var section = await service.GetOrCreateUploadSectionAsync("Imported images");
        section.KeepTarget = true;
        await service.CreateMediaAsync(new MediaFile("image.jpg", "hash-import-time"), section.Name);

        await service.ImportUploadSectionAsync(section.Id, null);

        var importedSection = await service.GetUploadSectionAsync(section.Id);
        Assert.NotNull(importedSection);
        Assert.NotNull(importedSection.LastImported);
    }

    [Fact]
    public async Task RemovingCollectionsToUncategorizedThenImportingSomeItemsKeepsCollectionItemsConsistent()
    {
        await using var connection = new SqliteConnection(
            $"Data Source=file:{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var setupContext = new AppDbContext(options);
        await setupContext.Database.EnsureCreatedAsync();
        await AppDbContext.SeedData(setupContext, CancellationToken.None);

        await using var firstContext = new AppDbContext(options);
        await using var secondContext = new AppDbContext(options);
        var firstService = SqliteTestHelpers.CreateService(firstContext);
        var secondService = SqliteTestHelpers.CreateService(secondContext);

        var firstCollectionId = await firstService.CreateCollection("First source", MediaFolder.RootFolderId);
        var secondCollectionId = await firstService.CreateCollection("Second source", MediaFolder.RootFolderId);
        var targetCollectionId = await firstService.CreateCollection("Target", MediaFolder.RootFolderId);
        var media = Enumerable.Range(1, 6)
            .Select(index => new MediaFile($"image-{index}.jpg", $"uncategorized-regression-{index}"))
            .ToList();

        foreach (var mediaFile in media.Take(3))
            await firstService.CreateMediaAsync(mediaFile, firstCollectionId);
        foreach (var mediaFile in media.Skip(3))
            await firstService.CreateMediaAsync(mediaFile, secondCollectionId);

        var firstRemoval = firstService.RemoveMediaFromCollectionAsync(firstCollectionId,
            media.Take(3).Select(item => item.Id).ToList());
        var secondRemoval = secondService.RemoveMediaFromCollectionAsync(secondCollectionId,
            media.Skip(3).Select(item => item.Id).ToList());
        await Task.WhenAll(firstRemoval, secondRemoval);

        var uncategorizedBeforeImport = await firstContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == Collection.UncategorizedCollectionId)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        Assert.Equal(media.Select(item => item.Id).ToHashSet(), uncategorizedBeforeImport.ToHashSet());

        var importedIds = media.Take(2).Select(item => item.Id).ToList();
        var targetSequence = await firstService.GetNextCollectionSequenceNumberAsync(targetCollectionId);
        await firstService.AddMediaToCollection(importedIds, targetCollectionId, targetSequence);

        var uncategorizedAfterImport = await firstContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == Collection.UncategorizedCollectionId)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        var targetItems = await firstContext.Set<CollectionItem>()
            .Where(item => item.CollectionId == targetCollectionId)
            .Select(item => item.MediaFileId)
            .ToListAsync();

        Assert.Equal(media.Skip(2).Select(item => item.Id).ToHashSet(), uncategorizedAfterImport.ToHashSet());
        Assert.Equal(importedIds.ToHashSet(), targetItems.ToHashSet());
    }
}
