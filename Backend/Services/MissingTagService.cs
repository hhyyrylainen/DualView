using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public sealed class MissingTagService : IMissingTagService, IDisposable
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IAppEvents appEvents;
    private readonly ILogger<MissingTagService> logger;
    private readonly Lock lockObject = new();
    private readonly List<MissingTagDTO> missingTags = new();

    public MissingTagService(IServiceScopeFactory scopeFactory, IAppEvents appEvents,
        ILogger<MissingTagService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.appEvents = appEvents;
        this.logger = logger;
        appEvents.TagCreated += OnTagCreated;
    }

    public async Task ReportTagAsync(string tag, MissingTagTarget target, long targetId)
    {
        tag = tag.Trim().ToLowerInvariant();
        if (tag.Length == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        if (await database.IsIgnoredTagAsync(tag))
            return;

        lock (lockObject)
        {
            if (missingTags.Any(item => item.Target == target && item.TargetId == targetId &&
                                        item.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            missingTags.Add(new MissingTagDTO { Tag = tag, Target = target, TargetId = targetId });

            // TODO: maybe this should have a max size like 5-10k items?
        }

        logger.LogWarning("Unknown tag '{Tag}' for {Target} {TargetId}", tag, target, targetId);
        await NotifyChangedAsync();
    }

    public Task<List<MissingTagDTO>> GetMissingTagsAsync()
    {
        lock (lockObject)
        {
            return Task.FromResult(missingTags.Select(item => new MissingTagDTO
            {
                Tag = item.Tag, Target = item.Target, TargetId = item.TargetId,
            }).ToList());
        }
    }

    public async Task IgnoreTagAsync(string tag)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        await database.AddIgnoredTagAsync(tag);
        lock (lockObject)
        {
            missingTags.RemoveAll(item => item.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
        }

        await NotifyChangedAsync();
    }

    public async Task ClearCurrentDetectionsAsync()
    {
        lock (lockObject)
        {
            missingTags.Clear();
        }

        await NotifyChangedAsync();
    }

    public async Task ResetIgnoredTagsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDatabaseService>().ClearIgnoredTagsAsync();
        await NotifyChangedAsync();
    }

    public void Dispose()
    {
        appEvents.TagCreated -= OnTagCreated;
    }

    private void OnTagCreated(string tagName, long tagId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyCreatedTagAsync(tagName, tagId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply newly created tag {TagId} to missing tag entries", tagId);
            }
        });
    }

    private async Task ApplyCreatedTagAsync(string tagName, long tagId)
    {
        List<MissingTagDTO> entries;
        lock (lockObject)
        {
            entries = missingTags.Where(item => item.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (entries.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var parser = scope.ServiceProvider.GetRequiredService<ITagParser>();
        var parsed = await parser.ParseTag(tagName);
        if (parsed == null)
        {
            logger.LogError("Failed to parse tag: {TagName} that should have been created!", tagName);
            return;
        }

        foreach (var entry in entries)
        {
            switch (entry.Target)
            {
                case MissingTagTarget.MediaFile:
                    await database.AddParsedAppliedTagToMediaAsync(entry.TargetId, parsed.GetDTO());
                    break;
                case MissingTagTarget.MediaImportInfo:
                    var importInfo = await database.GetMediaImportInfoAsync(entry.TargetId);
                    if (importInfo != null)
                        await database.AddParsedAppliedTagToMediaAsync(importInfo.MediaFileId, parsed.GetDTO());
                    break;
                case MissingTagTarget.DownloadGallery:
                    var gallery = await database.GetDownloadGalleryAsync(entry.TargetId);
                    if (gallery != null)
                        foreach (var import in gallery.AssociatedImports)
                            await database.AddParsedAppliedTagToMediaAsync(import.MediaFileId, parsed.GetDTO());
                    break;
            }
        }

        lock (lockObject)
        {
            missingTags.RemoveAll(item => item.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }

        await NotifyChangedAsync();
        logger.LogInformation("Applied newly created tag {TagId} to {Count} missing tag entries", tagId, entries.Count);
    }

    private async Task NotifyChangedAsync()
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEntityUpdateNotifier>().NotifyMissingTagsUpdated();
    }
}
