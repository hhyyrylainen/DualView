using Backend.Database;
using Backend.Services;
using DualView.Shared.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Database.Tests;

public class TagDatabaseTests
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
    public async Task CreateTag_EnforcesLowercase()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        // Act
        var tagId = await service.CreateTagAsync("TestTag", TagCategory.DescribeCharacterObject);

        // Assert
        var tag = await context.Tags.FindAsync(tagId);
        Assert.NotNull(tag);
        Assert.Equal("testtag", tag.Name);
    }

    [Fact]
    public async Task SearchTagsWildcard_ReturnsMatchingTags()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        await service.CreateTagAsync("Apple", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Banana", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Cherry", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Pineapple", TagCategory.DescribeCharacterObject);

        // Act
        var results = await service.SearchTagsWildcardAsync("apple");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, t => t.Name == "apple");
        Assert.Contains(results, t => t.Name == "pineapple");
    }

    [Fact]
    public async Task GetTagByName_IsCaseInsensitive()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        await service.CreateTagAsync("TestTag", TagCategory.DescribeCharacterObject);

        // Act
        var tag = await service.GetTagByNameAsync("TESTTAG");

        // Assert
        Assert.NotNull(tag);
        Assert.Equal("testtag", tag.Name);
    }

    [Fact]
    public async Task CreateTagAlias_EnforcesLowercase()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        var tagId = await service.CreateTagAsync("Test", TagCategory.DescribeCharacterObject);

        // Act
        await service.CreateTagAliasAsync(tagId, "AliasName");

        // Assert
        var alias = await context.TagAliases.FirstOrDefaultAsync(a => a.TagId == tagId);
        Assert.NotNull(alias);
        Assert.Equal("aliasname", alias.Name);
    }
}
