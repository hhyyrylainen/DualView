using Backend.Database;
using Backend.Models;
using Backend.Services;
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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
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
