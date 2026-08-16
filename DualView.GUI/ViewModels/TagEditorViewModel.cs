using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public sealed class TagEditorViewModel : ViewModelBase
{
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly List<long> targetIds = new();
    private Func<long, Task<List<AppliedTagDTO>>>? loadTags;
    private Func<long, long, Task>? addTag;
    private Func<long, long, Task>? removeTag;
    private int suggestionVersion;

    // Design-time constructor
    public TagEditorViewModel()
    {
        Suggestions.Add("Test tag");
        Suggestions.Add("example");

        // Add some example tags to show
        Tags.Add(new TagEditorRowViewModel("Test tag", 1, -1));
        Tags.Add(new TagEditorRowViewModel("Another tag", 42, -1));

        IsEnabled = true;
    }

    [ActivatorUtilitiesConstructor]
    public TagEditorViewModel(IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.databaseService = databaseService;
        this.windowService = windowService;
    }

    public ObservableCollection<TagEditorRowViewModel> Tags { get; } = new();
    public ObservableCollection<string> Suggestions { get; } = new();

    public string TagText
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public TagEditorRowViewModel? SelectedTag
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsEnabled
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public IBrush EntryBackground => IsInvalid ? Brushes.MistyRose : Brushes.Transparent;

    public bool IsInvalid
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(EntryBackground));
        }
    }

    public void Configure(IEnumerable<long> ids, Func<long, Task<List<AppliedTagDTO>>> load,
        Func<long, long, Task> add, Func<long, long, Task> remove)
    {
        targetIds.Clear();
        targetIds.AddRange(ids.Distinct());
        loadTags = load;
        addTag = add;
        removeTag = remove;
        IsEnabled = targetIds.Count > 0;
        _ = RefreshAsync();
    }

    public void ConfigureCollections(IEnumerable<long> ids)
    {
        if (databaseService == null)
            return;

        Configure(ids, databaseService.GetCollectionAppliedTagsAsync,
            async (targetId, tagId) => await databaseService.AddAppliedTagToCollectionAsync(
                targetId, tagId, null, null, null),
            databaseService.RemoveAppliedTagFromCollectionAsync);
    }

    public void ConfigureMedia(IEnumerable<long> ids)
    {
        if (databaseService == null)
            return;

        Configure(ids, databaseService.GetMediaAppliedTagsAsync,
            async (targetId, tagId) => await databaseService.AddAppliedTagToMediaAsync(
                targetId, tagId, null, null, null),
            databaseService.RemoveAppliedTagFromMediaAsync);
    }

    public async Task RefreshAsync()
    {
        Tags.Clear();
        SelectedTag = null;
        if (!IsEnabled || loadTags == null)
            return;

        var allTags = new List<(AppliedTagDTO Tag, int Count)>();
        foreach (var targetId in targetIds)
        {
            foreach (var tag in await loadTags(targetId))
            {
                var index = allTags.FindIndex(item => item.Tag.TagId == tag.TagId &&
                                                      string.Equals(item.Tag.Tag?.Name, tag.Tag?.Name,
                                                          StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    allTags.Add((tag, 1));
                }
                else
                {
                    allTags[index] = (allTags[index].Tag, allTags[index].Count + 1);
                }
            }
        }

        // TODO: need to convert tags to actual text representations!
        foreach (var item in allTags.OrderBy(item => item.Tag.Tag?.Name ?? string.Empty))
        {
            Tags.Add(new TagEditorRowViewModel(item.Tag.Tag?.Name ?? $"Tag {item.Tag.TagId}",
                item.Count, item.Tag.TagId));
        }
    }

    public async Task LoadSuggestionsAsync(string search)
    {
        var version = ++suggestionVersion;
        if (databaseService == null || search.Trim().Length == 0)
        {
            Suggestions.Clear();
            return;
        }

        // TODO: this is wrong, use TagParser
        var found = await databaseService.SearchTagsWildcardAsync(search);
        if (version != suggestionVersion)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Suggestions.Clear();
            foreach (var tag in found.Take(50))
                Suggestions.Add(tag.Name);
        });
    }

    public void AddTag()
    {
        _ = AddTagAsync(TagText);
    }

    public async Task AddTagAsync(string text)
    {
        if (databaseService == null || addTag == null || string.IsNullOrWhiteSpace(text))
            return;

        // TODO: this is wrong, use TagParser
        var tag = await databaseService.GetTagByNameAsync(text.Trim());
        if (tag == null)
        {
            await FlashInvalidAsync();
            return;
        }

        foreach (var targetId in targetIds)
        {
            var existingTags = loadTags == null ? [] : await loadTags(targetId);
            if (existingTags.All(existingTag => existingTag.TagId != tag.Id))
                await addTag(targetId, tag.Id);
        }

        TagText = string.Empty;
        await RefreshAsync();
    }

    public void DeleteSelected() => _ = DeleteSelectedAsync();

    public async Task DeleteSelectedAsync()
    {
        if (SelectedTag == null || removeTag == null)
            return;

        foreach (var targetId in targetIds)
        {
            var tags = loadTags == null ? [] : await loadTags(targetId);
            foreach (var tag in tags.Where(tag => tag.TagId == SelectedTag.AppliedTagId))
                await removeTag(targetId, tag.Id);
        }

        await RefreshAsync();
    }

    public void OpenTagManager()
    {
        var manager = windowService?.ShowSingletonWindow<TagManagerWindowViewModel>();
        if (manager != null)
            manager.BeginNewTag(TagText);
    }

    private async Task FlashInvalidAsync()
    {
        IsInvalid = true;
        await Task.Delay(450);
        IsInvalid = false;
    }
}

public sealed class TagEditorRowViewModel(string name, int count, long appliedTagId) : ViewModelBase
{
    public string Name { get; } = name;
    public int Count { get; } = count;
    public long AppliedTagId { get; } = appliedTagId;
}
