using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class TagManagerWindowViewModel : ViewModelBase
{
    private readonly ILogger<TagManagerWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private CancellationTokenSource? searchCts;

    public TagManagerWindowViewModel()
    {
        // Design time
        FoundTags.Add(new TagDTO("Test Tag") { Id = 1, Category = TagCategory.DescribeCharacterObject });
    }

    [ActivatorUtilitiesConstructor]
    public TagManagerWindowViewModel(ILogger<TagManagerWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;

        TagCategories = Enum.GetValues<TagCategory>().ToList();
    }

    public ObservableCollection<TagDTO> FoundTags { get; } = new();

    public List<TagCategory> TagCategories { get; } = new();

    public string SearchString
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                TriggerSearch();
            }
        }
    } = "";

    // New Tag Properties
    public string NewTagName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    SearchString = value.ToLowerInvariant();
                }
            }
        }
    } = "";

    public string NewTagDescription
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public TagCategory NewTagCategory
    {
        get;
        set => SetProperty(ref field, value);
    } = TagCategory.DescribeCharacterObject;

    public bool NewTagIsPrivate
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string NewTagAliases
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string NewTagImplies
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    // Edited Tag Properties
    public TagDTO? SelectedTag
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _ = LoadEditedTag(value);
            }
        }
    }

    public string EditTagName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EditTagDescription
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public TagCategory EditTagCategory
    {
        get;
        set => SetProperty(ref field, value);
    } = TagCategory.DescribeCharacterObject;

    public bool EditTagIsPrivate
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string EditTagAliases
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EditTagImplies
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public bool IsEditing => SelectedTag != null;

    public async Task UpdateSearch()
    {
        if (databaseService == null) return;

        try
        {
            var tags = await databaseService.SearchTagsWildcardAsync(SearchString);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FoundTags.Clear();
                foreach (var tag in tags)
                {
                    FoundTags.Add(tag);
                }
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to update tag search");
        }
    }

    public async Task CreateNewTag()
    {
        if (databaseService == null) return;

        try
        {
            var tagId = await databaseService.CreateTagAsync(NewTagName, NewTagCategory);
            await databaseService.UpdateTagAsync(tagId, NewTagName, NewTagDescription, NewTagCategory, null);
            
            // Handle aliases and implies
            var aliases = NewTagAliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var alias in aliases)
            {
                await databaseService.CreateTagAliasAsync(tagId, alias);
            }

            var implies = NewTagImplies.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var imply in implies)
            {
                var targetTag = await databaseService.GetTagByNameAsync(imply);
                if (targetTag != null)
                {
                    await databaseService.AddTagImplicationAsync(tagId, targetTag.Id);
                }
            }

            ClearNewTagEntry();
            await UpdateSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to create tag", e);
        }
    }

    public async Task SaveEditedTag()
    {
        if (databaseService == null || SelectedTag == null) return;

        try
        {
            await databaseService.UpdateTagAsync(SelectedTag.Id, EditTagName, EditTagDescription, EditTagCategory, null);
            
            // Handle aliases diff
            var currentAliases = await databaseService.GetTagAliasesAsync(SelectedTag.Id);
            var newAliases = EditTagAliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToLowerInvariant()).Distinct().ToList();

            foreach (var alias in newAliases.Where(a => !currentAliases.Contains(a)))
            {
                await databaseService.CreateTagAliasAsync(SelectedTag.Id, alias);
            }
            foreach (var alias in currentAliases.Where(a => !newAliases.Contains(a)))
            {
                await databaseService.DeleteTagAliasAsync(SelectedTag.Id, alias);
            }

            // Handle implies diff
            var currentImplies = await databaseService.GetTagImpliesAsync(SelectedTag.Id);
            var newImplyNames = EditTagImplies.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(i => i.ToLowerInvariant()).Distinct().ToList();

            foreach (var name in newImplyNames.Where(n => currentImplies.All(ci => ci.Name != n)))
            {
                var targetTag = await databaseService.GetTagByNameAsync(name);
                if (targetTag != null)
                {
                    await databaseService.AddTagImplicationAsync(SelectedTag.Id, targetTag.Id);
                }
            }
            foreach (var imply in currentImplies.Where(ci => !newImplyNames.Contains(ci.Name)))
            {
                await databaseService.RemoveTagImplicationAsync(SelectedTag.Id, imply.Id);
            }

            await UpdateSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to save tag", e);
        }
    }

    private void ClearNewTagEntry()
    {
        NewTagName = "";
        NewTagDescription = "";
        NewTagCategory = TagCategory.DescribeCharacterObject;
        NewTagIsPrivate = false;
        NewTagAliases = "";
        NewTagImplies = "";
    }

    private void TriggerSearch()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await UpdateSearch();
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async Task LoadEditedTag(TagDTO? tag)
    {
        if (tag == null)
        {
            EditTagName = "";
            EditTagDescription = "";
            EditTagCategory = TagCategory.DescribeCharacterObject;
            EditTagIsPrivate = false;
            EditTagAliases = "";
            EditTagImplies = "";
        }
        else
        {
            EditTagName = tag.Name;
            EditTagDescription = tag.Description ?? "";
            EditTagCategory = tag.Category;
            
            if (databaseService != null)
            {
                try
                {
                    var aliases = await databaseService.GetTagAliasesAsync(tag.Id);
                    EditTagAliases = string.Join('\n', aliases);

                    var implies = await databaseService.GetTagImpliesAsync(tag.Id);
                    EditTagImplies = string.Join('\n', implies.Select(t => t.Name));
                }
                catch (Exception e)
                {
                    logger?.LogError(e, "Failed to load tag aliases or implies for tag {TagId}", tag.Id);
                }
            }
        }

        OnPropertyChanged(nameof(IsEditing));
    }
}
