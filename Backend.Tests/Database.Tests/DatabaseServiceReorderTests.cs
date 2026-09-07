using Backend.Database;
using Backend.Models;
using Backend.Services;
using DualView.Shared.Requests;
using DualView.Shared.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Database.Tests;

public class DatabaseServiceReorderTests
{
    private readonly IEntityUpdateNotifier updateNotifier = Substitute.For<IEntityUpdateNotifier>();
    private readonly ILogger<DatabaseService> logger = Substitute.For<ILogger<DatabaseService>>();
    private readonly IAppEvents appEvents = Substitute.For<IAppEvents>();
    private readonly IDataFolderService dataFolderService = Substitute.For<IDataFolderService>();
    private readonly IMediaProcessingService mediaProcessingService = Substitute.For<IMediaProcessingService>();

    private AppDbContext CreateDbContext()
    {
        return SqliteTestHelpers.CreateContext();
    }

    [Fact]
    public async Task SetUploadSectionActive_ClearsPreviousSectionBeforeSelectingNewOne()
    {
        using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var firstSection = new UploadSection("First") { Selected = true, DisplayIndex = 0 };
        var secondSection = new UploadSection("Second") { DisplayIndex = 1 };
        await context.UploadSections.AddRangeAsync(firstSection, secondSection);
        await context.SaveChangesAsync();

        await service.SetUploadSectionActiveAsync(secondSection.Id);

        Assert.False(await context.UploadSections.Where(section => section.Id == firstSection.Id)
            .Select(section => section.Selected).SingleAsync());
        Assert.True(await context.UploadSections.Where(section => section.Id == secondSection.Id)
            .Select(section => section.Selected).SingleAsync());
    }

    [Fact]
    public async Task SetUploadSectionActive_RejectsMissingSectionWithoutChangingCurrentSelection()
    {
        using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var section = new UploadSection("Current") { Selected = true };
        await context.UploadSections.AddAsync(section);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetUploadSectionActiveAsync(999));

        Assert.True(await context.UploadSections.Where(item => item.Id == section.Id)
            .Select(item => item.Selected).SingleAsync());
    }

    [Fact]
    public async Task ReorderCollection_DoesNotRemoveItems()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        var collection = new Collection("Test");
        await context.Collections.AddAsync(collection);

        var media1 = new MediaFile("p1", "h1") { Id = 10 };
        var media2 = new MediaFile("p2", "h2") { Id = 20 };
        var media3 = new MediaFile("p3", "h3") { Id = 30 };
        await context.MediaFiles.AddRangeAsync(media1, media2, media3);

        collection.Items.Add(new CollectionItem { Collection = collection, MediaFile = media1, SequenceNumber = 0 });
        collection.Items.Add(new CollectionItem { Collection = collection, MediaFile = media2, SequenceNumber = 1 });
        collection.Items.Add(new CollectionItem { Collection = collection, MediaFile = media3, SequenceNumber = 2 });

        await context.SaveChangesAsync();

        // Act - Reorder only media2 and media1
        await service.ReorderCollection(collection.Id, new List<long> { 20, 10 });

        // Assert
        var updatedCollection = await context.Collections.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == collection.Id);

        Assert.NotNull(updatedCollection);
        Assert.Equal(3, updatedCollection.Items.Count); // Should still have 3 items

        var items = updatedCollection.Items.OrderBy(i => i.SequenceNumber).ToList();
        Assert.Equal(20, items[0].MediaFileId); // Media 2 first
        Assert.Equal(10, items[1].MediaFileId); // Media 1 second
        Assert.Equal(30, items[2].MediaFileId); // Media 3 should be kept at the end

        Assert.Equal(0, items[0].SequenceNumber);
        Assert.Equal(1, items[1].SequenceNumber);
        Assert.Equal(2, items[2].SequenceNumber);
    }

    [Fact]
    public async Task ReorderCollectionPage_ReplacesVisiblePageAtItsOriginalPosition()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var collection = new Collection("Test");
        var media = Enumerable.Range(1, 5)
            .Select(index => new MediaFile($"p{index}", $"h{index}") { Id = index * 10 })
            .ToList();
        await context.Collections.AddAsync(collection);
        await context.MediaFiles.AddRangeAsync(media);
        foreach (var (item, index) in media.Select((item, index) => (item, index)))
        {
            collection.Items.Add(new CollectionItem
            {
                Collection = collection,
                MediaFile = item,
                SequenceNumber = index,
            });
        }
        media[2].IsDeleted = true;
        await context.SaveChangesAsync();

        await service.ReorderCollectionPageAsync(collection.Id, new CollectionReorderRequest
        {
            MediaIds = [40, 20, 30],
        });

        var order = await context.Set<CollectionItem>().IgnoreQueryFilters()
            .Where(item => item.CollectionId == collection.Id)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        Assert.Equal([10, 40, 20, 30, 50], order);
    }

    [Fact]
    public async Task ReorderCollectionPage_NormalizesDescendingCollectionOrder()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var collection = new Collection("Test");
        var media = Enumerable.Range(1, 5)
            .Select(index => new MediaFile($"p{index}", $"h{index}") { Id = index * 10 })
            .ToList();
        await context.Collections.AddAsync(collection);
        await context.MediaFiles.AddRangeAsync(media);
        foreach (var (item, index) in media.Select((item, index) => (item, index)))
        {
            collection.Items.Add(new CollectionItem
            {
                Collection = collection,
                MediaFile = item,
                SequenceNumber = index,
            });
        }
        await context.SaveChangesAsync();

        await service.ReorderCollectionPageAsync(collection.Id, new CollectionReorderRequest
        {
            MediaIds = [40, 50, 30],
            SortColumn = CollectionSortColumn.CollectionOrder,
            SortDirection = SortDirection.Descending,
        });

        var order = await context.Set<CollectionItem>().IgnoreQueryFilters()
            .Where(item => item.CollectionId == collection.Id)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        Assert.Equal([10, 20, 30, 50, 40], order);
    }

    [Fact]
    public async Task ReorderCollectionPage_PersistsReorderMadeInNameOrder()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var collection = new Collection("Test");
        var media = new List<MediaFile>
        {
            new("Charlie", "h30") { Id = 30 },
            new("Alpha", "h10") { Id = 10 },
            new("Bravo", "h20") { Id = 20 },
        };
        await context.Collections.AddAsync(collection);
        await context.MediaFiles.AddRangeAsync(media);
        foreach (var (item, index) in media.Select((item, index) => (item, index)))
        {
            collection.Items.Add(new CollectionItem
            {
                Collection = collection,
                MediaFile = item,
                SequenceNumber = index,
            });
        }
        await context.SaveChangesAsync();

        // Name order is [10, 20, 30]. Move 20 before 10 and save that display order.
        await service.ReorderCollectionPageAsync(collection.Id, new CollectionReorderRequest
        {
            MediaIds = [20, 10, 30],
            SortColumn = CollectionSortColumn.Name,
            SortDirection = SortDirection.Ascending,
        });

        var order = await context.Set<CollectionItem>().IgnoreQueryFilters()
            .Where(item => item.CollectionId == collection.Id)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        Assert.Equal([20, 10, 30], order);
    }

    [Fact]
    public async Task ReorderCollectionPage_OnlyChangesItemsOnTheSelectedPage()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var collection = new Collection("Test");
        var media = Enumerable.Range(1, 100)
            .Select(index => new MediaFile($"p{index}", $"h{index}") { Id = index })
            .ToList();
        await context.Collections.AddAsync(collection);
        await context.MediaFiles.AddRangeAsync(media);
        foreach (var (item, index) in media.Select((item, index) => (item, index)))
        {
            collection.Items.Add(new CollectionItem
            {
                Collection = collection,
                MediaFile = item,
                SequenceNumber = index,
            });
        }
        await context.SaveChangesAsync();

        // Page 2 with page size 25 contains items 26 through 50. Reorder only three of them.
        var pageOrder = Enumerable.Range(26, 25).Select(index => (long)index).ToList();
        pageOrder.Remove(30);
        pageOrder.Remove(35);
        pageOrder.Remove(40);
        pageOrder.InsertRange(4, [40, 30, 35]);

        await service.ReorderCollectionPageAsync(collection.Id, new CollectionReorderRequest
        {
            MediaIds = pageOrder,
            SortColumn = CollectionSortColumn.CollectionOrder,
            SortDirection = SortDirection.Ascending,
        });

        var order = await context.Set<CollectionItem>().IgnoreQueryFilters()
            .Where(item => item.CollectionId == collection.Id)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.MediaFileId)
            .ToListAsync();
        var expectedOrder = Enumerable.Range(1, 25).Select(index => (long)index)
            .Concat(pageOrder)
            .Concat(Enumerable.Range(51, 50).Select(index => (long)index));
        Assert.Equal(expectedOrder, order);
    }

    [Fact]
    public async Task ReorderUploadSection_IncludesDeletedItemsInOrder()
    {
        // Arrange
        using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        var section = new UploadSection("Test");
        var media1 = new MediaFile("p1", "h1") { Id = 10 };
        var deletedMedia = new MediaFile("p2", "h2") { Id = 20 };
        var media3 = new MediaFile("p3", "h3") { Id = 30 };
        await context.UploadSections.AddAsync(section);
        await context.MediaFiles.AddRangeAsync(media1, deletedMedia, media3);
        await context.UploadSectionItems.AddRangeAsync(
            new UploadSectionItem { UploadSection = section, MediaFile = media1, Index = 0 },
            new UploadSectionItem { UploadSection = section, MediaFile = deletedMedia, Index = 1 },
            new UploadSectionItem { UploadSection = section, MediaFile = media3, Index = 2 });
        await context.SaveChangesAsync();
        await service.DeleteMediaAsync(deletedMedia.Id);

        // Act
        await service.ReorderUploadSectionAsync(section.Id, new List<long> { 30, 10 });

        // Assert
        var items = await context.UploadSectionItems
            .IgnoreQueryFilters()
            .Where(item => item.UploadSectionId == section.Id)
            .OrderBy(item => item.Index)
            .ToListAsync();
        Assert.Equal([30, 10, 20], items.Select(item => item.MediaFileId));
    }

    [Fact]
    public async Task GetCollectionContents_DefaultsToCollectionOrder()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        var collection = new Collection("Test");
        var media1 = new MediaFile("z-file", "h1") { Id = 10 };
        var media2 = new MediaFile("a-file", "h2") { Id = 20 };
        await context.Collections.AddAsync(collection);
        await context.MediaFiles.AddRangeAsync(media1, media2);

        collection.Items.Add(new CollectionItem { Collection = collection, MediaFile = media1, SequenceNumber = 0 });
        collection.Items.Add(new CollectionItem { Collection = collection, MediaFile = media2, SequenceNumber = 1 });
        await context.SaveChangesAsync();

        // Act
        var (items, total) = await service.GetCollectionContents(collection.Id, 0, 10);

        // Assert
        Assert.Equal(2, total);
        Assert.Equal([10, 20], items.Select(item => item.Id));
    }
}
