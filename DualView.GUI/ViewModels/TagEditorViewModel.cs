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
using DualView.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public sealed class TagEditorViewModel : ViewModelBase
{
    public delegate Task<List<AppliedTagDTO>> LoadTagsDelegate(long targetId);

    public delegate Task AddTagDelegate(long targetId, AppliedTagDTO tag);

    public delegate Task RemoveTagDelegate(long targetId, long appliedTagId);

    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly List<long> targetIds = new();

    private LoadTagsDelegate? loadTags;
    private AddTagDelegate? addTag;
    private RemoveTagDelegate? removeTag;

    private int suggestionVersion;

    // Design-time constructor
    public TagEditorViewModel()
    {
        Suggestions = ["Test tag", "example"];

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

    public List<string> Suggestions
    {
        get;
        private set => SetProperty(ref field, value);
    } = new();

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

    public void Configure(IEnumerable<long> ids, LoadTagsDelegate load, AddTagDelegate add, RemoveTagDelegate remove)
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

        Configure(ids, databaseService.GetCollectionAppliedTagsAsync, AddCollectionTagAsync,
            databaseService.RemoveAppliedTagFromCollectionAsync);
    }

    public void ConfigureMedia(IEnumerable<long> ids)
    {
        if (databaseService == null)
            return;

        Configure(ids, databaseService.GetMediaAppliedTagsAsync, AddMediaTagAsync,
            databaseService.RemoveAppliedTagFromMediaAsync);
    }

    public void ConfigureUploadSections(IEnumerable<long> ids)
    {
        if (databaseService == null)
            return;

        Configure(ids, databaseService.GetUploadSectionAppliedTagsAsync, AddUploadSectionTagAsync,
            databaseService.RemoveAppliedTagFromUploadSectionAsync);
    }

    public async Task RefreshAsync()
    {
        Tags.Clear();
        SelectedTag = null;
        if (!IsEnabled || loadTags == null)
            return;

        var allTags = new List<(AppliedTagDTO Tag, string Text, int Count)>();
        foreach (var targetId in targetIds)
        {
            foreach (var tag in await loadTags(targetId))
            {
                var text = AppliedTagText.ToText(tag);
                var index = allTags.FindIndex(item => string.Equals(item.Text, text,
                    StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    allTags.Add((tag, text, 1));
                }
                else
                {
                    allTags[index] = (allTags[index].Tag, allTags[index].Text, allTags[index].Count + 1);
                }
            }
        }

        foreach (var item in allTags.OrderBy(item => item.Text, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(new TagEditorRowViewModel(item.Text, item.Count, item.Tag.Id));
        }
    }

    public async Task LoadSuggestionsAsync(string search)
    {
        var version = ++suggestionVersion;
        if (databaseService == null || search.Trim().Length == 0)
        {
            Suggestions = [];
            return;
        }

        var found = await databaseService.GetTagSuggestionsAsync(search, 100);
        if (version != suggestionVersion)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => { Suggestions = found; });
    }

    public void AddTag()
    {
        _ = AddTagAsync(TagText);
    }

    public async Task AddTagAsync(string text)
    {
        if (databaseService == null || addTag == null || string.IsNullOrWhiteSpace(text))
            return;

        AppliedTagDTO? appliedTag;
        try
        {
            appliedTag = await databaseService.ParseTagAsync(text.Trim());
            if (appliedTag == null)
            {
                await FlashInvalidAsync();
                return;
            }
        }
        catch (Exception)
        {
            // Assume server returned an error / can't parse state
            await FlashInvalidAsync();
            return;
        }

        var appliedText = AppliedTagText.ToText(appliedTag);

        foreach (var targetId in targetIds)
        {
            // TODO: this might be too slow to reload all tags for everything
            var existingTags = loadTags == null ? [] : await loadTags(targetId);
            if (existingTags.All(existingTag => !string.Equals(AppliedTagText.ToText(existingTag),
                    appliedText, StringComparison.OrdinalIgnoreCase)))
            {
                await addTag(targetId, appliedTag);
            }
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
            foreach (var tag in tags.Where(tag => string.Equals(AppliedTagText.ToText(tag), SelectedTag.Name,
                         StringComparison.OrdinalIgnoreCase)))
            {
                await removeTag(targetId, tag.Id);
            }
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

    private Task AddCollectionTagAsync(long targetId, AppliedTagDTO tag)
    {
        return databaseService!.AddParsedAppliedTagToCollectionAsync(targetId, tag);
    }

    private Task AddMediaTagAsync(long targetId, AppliedTagDTO tag)
    {
        return databaseService!.AddParsedAppliedTagToMediaAsync(targetId, tag);
    }

    private Task AddUploadSectionTagAsync(long targetId, AppliedTagDTO tag)
    {
        return databaseService!.AddParsedAppliedTagToUploadSectionAsync(targetId, tag);
    }
}

public sealed class TagEditorRowViewModel(string name, int count, long appliedTagId) : ViewModelBase
{
    public string Name { get; } = name;
    public int Count { get; } = count;
    public long AppliedTagId { get; } = appliedTagId;
}
