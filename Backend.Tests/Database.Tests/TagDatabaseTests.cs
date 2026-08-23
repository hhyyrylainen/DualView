using Backend.Database;
using Backend.Models;
using Backend.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Utils;
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
    public async Task CreateAndUpdateTag_DisallowCommas()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateTagAsync("tag,withcomma", TagCategory.DescribeCharacterObject));

        var tagId = await service.CreateTagAsync("validtag", TagCategory.DescribeCharacterObject);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTagAsync(tagId, "tag,withcomma",
            null, null, null));
    }

    [Fact]
    public async Task CreateAndUpdateTagModifier_DisallowCommas()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTagModifierAsync("modifier,withcomma"));

        var modifierId = await service.CreateTagModifierAsync("validmodifier");
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTagModifierAsync(modifierId,
            "modifier,withcomma", null));
    }

    [Fact]
    public async Task CreateTagAlias_DisallowsCommas()
    {
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);
        var tagId = await service.CreateTagAsync("validtag", TagCategory.DescribeCharacterObject);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTagAliasAsync(tagId, "alias,withcomma"));
    }

    [Fact]
    public async Task SearchTagsWildcard_ReturnsMatchingTags()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new DatabaseService(logger, context, updateNotifier,
            appEvents, dataFolderService, mediaProcessingService);

        await service.CreateTagAsync("Apple", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Apple Pie", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Banana", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Cherry", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Pineapple", TagCategory.DescribeCharacterObject);
        await service.CreateTagAsync("Zapple", TagCategory.DescribeCharacterObject);

        // Act
        var results = await service.SearchTagsWildcardAsync("apple");

        // Assert
        Assert.Equal(["apple", "apple pie", "zapple", "pineapple"], results.Select(tag => tag.Name));
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

    [Fact]
    public async Task ParsedAppliedTags_AreSharedAndRenderCompleteText()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var manTagId = await service.CreateTagAsync("man", TagCategory.DescribeCharacterObject);
        var tableTagId = await service.CreateTagAsync("table", TagCategory.DescribeCharacterObject);
        var youngModifierId = await service.CreateTagModifierAsync("young");
        var parser = new TagParser(service);

        var sharedTag = await parser.ParseTag("man");
        var modifiedTag = await parser.ParseTag("young man");
        var combinedTag = await parser.ParseTag("man on table");

        Assert.NotNull(sharedTag);
        Assert.NotNull(modifiedTag);
        Assert.NotNull(combinedTag);
        Assert.Equal(manTagId, sharedTag.TagId);
        Assert.Contains(modifiedTag.Modifiers, modifier => modifier.Id == youngModifierId);
        Assert.Equal(manTagId, combinedTag.TagId);
        Assert.NotNull(combinedTag.CombinedWith);
        Assert.Equal(tableTagId, combinedTag.CombinedWith.TagId);
        Assert.Equal("on", combinedTag.CombineWord);

        var collectionId = await service.CreateCollection("Tag sharing collection", MediaFolder.RootFolderId);
        var section = await service.GetOrCreateUploadSectionAsync("Tag import section");

        await service.AddParsedAppliedTagToCollectionAsync(collectionId, sharedTag.GetDTO());
        await service.AddParsedAppliedTagToUploadSectionAsync(section.Id, sharedTag.GetDTO());
        await service.AddParsedAppliedTagToCollectionAsync(collectionId, modifiedTag.GetDTO());
        await service.AddParsedAppliedTagToUploadSectionAsync(section.Id, combinedTag.GetDTO());
        var appliedTagCount = await context.AppliedTags.CountAsync();
        await service.AddParsedAppliedTagToUploadSectionAsync(section.Id, sharedTag.GetDTO());
        await service.AddParsedAppliedTagToUploadSectionAsync(section.Id, sharedTag.GetDTO());

        Assert.Equal(appliedTagCount, await context.AppliedTags.CountAsync());

        var collectionTags = (await service.GetCollectionAppliedTagsAsync(collectionId))
            .Select(tag => tag.GetDTO()).ToList();
        var sectionTags = (await service.GetUploadSectionAppliedTagsAsync(section.Id))
            .Select(tag => tag.GetDTO()).ToList();
        var sharedCollectionTag = Assert.Single(collectionTags, tag => tag.TagId == manTagId &&
                                                                       tag.Modifiers.Count == 0 &&
                                                                       tag.CombinedWith == null);
        var sharedSectionTag = Assert.Single(sectionTags, tag => tag.TagId == manTagId &&
                                                                 tag.Modifiers.Count == 0 && tag.CombinedWith == null);

        Assert.Equal(sharedCollectionTag.Id, sharedSectionTag.Id);
        Assert.Contains(collectionTags, tag => AppliedTagText.ToText(tag) == "young man");
        Assert.Contains(sectionTags, tag => AppliedTagText.ToText(tag) == "man on table");

        await service.RemoveAppliedTagFromCollectionAsync(collectionId, sharedCollectionTag.Id);

        Assert.DoesNotContain(await service.GetCollectionAppliedTagsAsync(collectionId),
            tag => tag.Id == sharedCollectionTag.Id);
        Assert.Contains(await service.GetUploadSectionAppliedTagsAsync(section.Id),
            tag => tag.Id == sharedSectionTag.Id);
        Assert.NotNull(await service.GetAppliedTagAsync(sharedCollectionTag.Id));
    }

    [Fact]
    public async Task Parser_ResolvesMultiWordModifierBeforeTag()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var modifierId = await service.CreateTagModifierAsync("very long");
        var tagId = await service.CreateTagAsync("hair", TagCategory.DescribeCharacterObject);
        var parser = new TagParser(service);
        var media = new MediaFile("hair.jpg", "hair-hash");
        context.MediaFiles.Add(media);
        await context.SaveChangesAsync();

        var parsed = await parser.ParseTag("very long hair");

        Assert.NotNull(parsed);
        Assert.Equal(tagId, parsed.TagId);
        Assert.Contains(parsed.Modifiers, modifier => modifier.Id == modifierId);

        await service.AddParsedAppliedTagToMediaAsync(media.Id, parsed.GetDTO());

        var mediaTags = await service.GetMediaAppliedTagsAsync(media.Id);
        var appliedTag = Assert.Single(mediaTags);
        Assert.Equal(tagId, appliedTag.TagId);
        Assert.Contains(appliedTag.Modifiers, modifier => modifier.Id == modifierId);
    }

    [Fact]
    public async Task UploadSection_DoesNotAddEquivalentAppliedTagTwice()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var tagId = await service.CreateTagAsync("duplicate test", TagCategory.DescribeCharacterObject);
        var section = await service.GetOrCreateUploadSectionAsync("Duplicate tag section");

        var firstAppliedTag = new AppliedTag(tagId);
        var secondAppliedTag = new AppliedTag(tagId);
        context.AppliedTags.AddRange(firstAppliedTag, secondAppliedTag);
        await context.SaveChangesAsync();
        section.AppliedTags.Add(firstAppliedTag);
        await context.SaveChangesAsync();

        var returnedId = await service.AddAppliedTagToUploadSectionAsync(section.Id, tagId, null, null, null);
        var sectionTags = await service.GetUploadSectionAppliedTagsAsync(section.Id);

        Assert.Contains(returnedId, new[] { firstAppliedTag.Id, secondAppliedTag.Id });
        Assert.Single(sectionTags);
        Assert.Equal(2, await context.AppliedTags.CountAsync());
    }

    [Fact]
    public async Task BulkMediaTagging_ReusesAppliedTagForOverlappingMedia()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var tagId = await service.CreateTagAsync("bulk tag", TagCategory.DescribeCharacterObject);

        var firstMedia = new MediaFile("first.jpg", "bulk-hash-1");
        var secondMedia = new MediaFile("second.jpg", "bulk-hash-2");
        var thirdMedia = new MediaFile("third.jpg", "bulk-hash-3");
        context.MediaFiles.AddRange(firstMedia, secondMedia, thirdMedia);
        await context.SaveChangesAsync();

        var tag = new AppliedTagDTO(0, tagId);
        await service.AddParsedAppliedTagsToMediaAsync(
            [firstMedia.Id, secondMedia.Id], [tag]);

        Assert.Equal(1, await context.AppliedTags.CountAsync());
        Assert.All(new[] { firstMedia.Id, secondMedia.Id }, mediaId =>
            Assert.Single(context.MediaFiles
                .Where(media => media.Id == mediaId)
                .SelectMany(media => media.AppliedTags)));

        await service.AddParsedAppliedTagsToMediaAsync(
            [firstMedia.Id, secondMedia.Id, thirdMedia.Id], [tag]);

        Assert.Equal(1, await context.AppliedTags.CountAsync());
        foreach (var mediaId in new[] { firstMedia.Id, secondMedia.Id, thirdMedia.Id })
        {
            var mediaTags = await service.GetMediaAppliedTagsAsync(mediaId);
            Assert.Single(mediaTags);
            Assert.Equal(tagId, mediaTags[0].TagId);
        }
    }

    [Fact]
    public async Task MergeTag_DoesNotDuplicateAppliedTagWhenMediaHasBothTags()
    {
        await using var context = SqliteTestHelpers.CreateContext(seed: true);
        var service = SqliteTestHelpers.CreateService(context);
        var sourceTagId = await service.CreateTagAsync("source tag", TagCategory.DescribeCharacterObject);
        var targetTagId = await service.CreateTagAsync("target tag", TagCategory.DescribeCharacterObject);
        var media = new MediaFile("merge.jpg", "merge-hash");
        context.MediaFiles.Add(media);
        await context.SaveChangesAsync();

        await service.AddParsedAppliedTagToMediaAsync(media.Id, new AppliedTagDTO(0, sourceTagId));
        await service.AddParsedAppliedTagToMediaAsync(media.Id, new AppliedTagDTO(0, targetTagId));

        await service.MergeTagAsync(sourceTagId, targetTagId);

        var mediaTags = await service.GetMediaAppliedTagsAsync(media.Id);
        Assert.Single(mediaTags);
        Assert.Equal(targetTagId, mediaTags[0].TagId);
        Assert.Equal(1, await context.AppliedTags.CountAsync());
    }
}
