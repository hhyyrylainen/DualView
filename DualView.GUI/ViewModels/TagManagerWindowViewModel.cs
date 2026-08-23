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
    private CancellationTokenSource? modifierSearchCts;
    private CancellationTokenSource? superAliasSearchCts;

    public TagManagerWindowViewModel()
    {
        // Design time
        FoundTags.Add(new TagDTO("Test Tag") { Id = 1, Category = TagCategory.DescribeCharacterObject });
        FoundModifiers.Add(new TagModifierDTO("Test modifier") { Id = 1 });
        FoundSuperAliases.Add(new TagSuperAliasDTO("test alias", "test tag"));
    }

    [ActivatorUtilitiesConstructor]
    public TagManagerWindowViewModel(ILogger<TagManagerWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;

        TagCategories = Enum.GetValues<TagCategory>().ToList();
        _ = UpdateModifierSearch();
        _ = UpdateSuperAliasSearch();
    }

    public ObservableCollection<TagDTO> FoundTags { get; } = new();

    public ObservableCollection<TagModifierDTO> FoundModifiers { get; } = new();

    public ObservableCollection<TagSuperAliasDTO> FoundSuperAliases { get; } = new();

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

    public string ModifierSearchString
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                TriggerModifierSearch();
        }
    } = "";

    public string SuperAliasSearchString
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                TriggerSuperAliasSearch();
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

    public TagModifierDTO? SelectedModifier
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                LoadEditedModifier(value);
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

    public bool IsMergingTag
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string MergeIntoTagName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public bool IsEditingModifier => SelectedModifier != null;

    public bool IsEditingSuperAlias => SelectedSuperAlias != null;

    // New Modifier Properties
    public string NewModifierName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string NewModifierDescription
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    // Edited Modifier Properties
    public string EditModifierName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EditModifierDescription
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public TagSuperAliasDTO? SelectedSuperAlias
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                LoadEditedSuperAlias(value);
        }
    }

    public string NewSuperAlias
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string NewSuperAliasExpanded
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EditSuperAlias
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EditSuperAliasExpanded
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

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

    public async Task UpdateModifierSearch()
    {
        if (databaseService == null)
            return;

        try
        {
            var search = ModifierSearchString.Trim();

            // There are few enough modifiers that we can load them all into memory, but otherwise this would be very bad style.
            var modifiers = await databaseService.GetAllTagModifiersAsync();
            var matchingModifiers = modifiers
                .Where(modifier => modifier.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(modifier => modifier.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(modifier => modifier.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase))
                .ThenBy(modifier => modifier.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FoundModifiers.Clear();
                foreach (var modifier in matchingModifiers)
                    FoundModifiers.Add(modifier);
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to update tag modifier search");
        }
    }

    public void BeginNewTag(string name)
    {
        NewTagName = name.Trim();
        if (!string.IsNullOrWhiteSpace(NewTagName))
            SearchString = NewTagName.ToLowerInvariant();
    }

    public async Task CreateNewTag()
    {
        if (databaseService == null) return;

        try
        {
            var impliedTagIds = await ParseImpliedTagIdsAsync(NewTagImplies);
            var tagId = await databaseService.CreateTagAsync(NewTagName, NewTagCategory);
            await databaseService.UpdateTagAsync(tagId, NewTagName, NewTagDescription, NewTagCategory, null);

            // Handle aliases and implies
            var aliases = NewTagAliases.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var alias in aliases)
            {
                await databaseService.CreateTagAliasAsync(tagId, alias);
            }

            foreach (var impliedTagId in impliedTagIds)
            {
                await databaseService.AddTagImplicationAsync(tagId, impliedTagId);
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
            var newImpliedTagIds = await ParseImpliedTagIdsAsync(EditTagImplies);
            var currentAliases = await databaseService.GetTagAliasesAsync(SelectedTag.Id);
            var currentImplies = await databaseService.GetTagImpliesAsync(SelectedTag.Id);

            await databaseService.UpdateTagAsync(SelectedTag.Id, EditTagName, EditTagDescription, EditTagCategory,
                null);

            // Handle aliases diff
            var newAliases = EditTagAliases
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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
            foreach (var impliedTagId in newImpliedTagIds.Where(id => currentImplies.All(tag => tag.Id != id)))
            {
                await databaseService.AddTagImplicationAsync(SelectedTag.Id, impliedTagId);
            }

            foreach (var imply in currentImplies.Where(tag => !newImpliedTagIds.Contains(tag.Id)))
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

    public void BeginMergeTag()
    {
        MergeIntoTagName = "";
        IsMergingTag = true;
    }

    public async Task MergeSelectedTag()
    {
        if (databaseService == null || SelectedTag == null)
            return;

        try
        {
            var targetTag = await databaseService.GetTagByNameAsync(MergeIntoTagName.Trim());
            if (targetTag == null)
                throw new ArgumentException("The target tag was not found.");

            await databaseService.MergeTagAsync(SelectedTag.Id, targetTag.Id);
            IsMergingTag = false;
            MergeIntoTagName = "";
            SelectedTag = null;
            await UpdateSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to merge tag", e);
        }
    }

    public async Task CreateNewModifier()
    {
        if (databaseService == null)
            return;

        try
        {
            var modifierId = await databaseService.CreateTagModifierAsync(NewModifierName);
            await databaseService.UpdateTagModifierAsync(modifierId, NewModifierName, NewModifierDescription);
            ClearNewModifierEntry();
            await UpdateModifierSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to create tag modifier", e);
        }
    }

    public async Task UpdateSuperAliasSearch()
    {
        if (databaseService == null)
            return;

        try
        {
            var search = SuperAliasSearchString.Trim();
            var aliases = await databaseService.GetAllTagSuperAliasesAsync();
            var matchingAliases = aliases
                .Where(alias => alias.Alias.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(alias => alias.Alias.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(alias => alias.Alias.IndexOf(search, StringComparison.OrdinalIgnoreCase))
                .ThenBy(alias => alias.Alias, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FoundSuperAliases.Clear();
                foreach (var alias in matchingAliases)
                    FoundSuperAliases.Add(alias);
            });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to update tag super alias search");
        }
    }

    public async Task CreateNewSuperAlias()
    {
        if (databaseService == null)
            return;

        try
        {
            await databaseService.CreateTagSuperAliasAsync(NewSuperAlias, NewSuperAliasExpanded);
            ClearNewSuperAliasEntry();
            await UpdateSuperAliasSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to create tag super alias", e);
        }
    }

    public async Task SaveEditedSuperAlias()
    {
        if (databaseService == null || SelectedSuperAlias == null)
            return;

        try
        {
            await databaseService.UpdateTagSuperAliasAsync(SelectedSuperAlias.Alias, EditSuperAlias,
                EditSuperAliasExpanded);
            SelectedSuperAlias = null;
            await UpdateSuperAliasSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to save tag super alias", e);
        }
    }

    public async Task SaveEditedModifier()
    {
        if (databaseService == null || SelectedModifier == null)
            return;

        try
        {
            await databaseService.UpdateTagModifierAsync(SelectedModifier.Id, EditModifierName,
                EditModifierDescription);
            await UpdateModifierSearch();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to save tag modifier", e);
        }
    }

    private void ClearNewTagEntry()
    {
        NewTagName = "";
        NewTagDescription = "";
        NewTagCategory = TagCategory.DescribeCharacterObject;
        NewTagAliases = "";
        NewTagImplies = "";
    }

    private async Task<List<long>> ParseImpliedTagIdsAsync(string text)
    {
        var implies = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var impliedTagIds = new List<long>();

        foreach (var imply in implies)
        {
            var parsedTag = await databaseService!.ParseTagAsync(imply);
            if (parsedTag == null)
            {
                throw new ArgumentException($"Could not parse implied tag '{imply}'.");
            }

            if (!impliedTagIds.Contains(parsedTag.TagId))
            {
                impliedTagIds.Add(parsedTag.TagId);
            }
        }

        return impliedTagIds;
    }

    private void ClearNewModifierEntry()
    {
        NewModifierName = "";
        NewModifierDescription = "";
    }

    private void ClearNewSuperAliasEntry()
    {
        NewSuperAlias = "";
        NewSuperAliasExpanded = "";
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

    private void TriggerModifierSearch()
    {
        modifierSearchCts?.Cancel();
        modifierSearchCts = new CancellationTokenSource();
        var token = modifierSearchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await UpdateModifierSearch();
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void TriggerSuperAliasSearch()
    {
        superAliasSearchCts?.Cancel();
        superAliasSearchCts = new CancellationTokenSource();
        var token = superAliasSearchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await UpdateSuperAliasSearch();
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

    private void LoadEditedModifier(TagModifierDTO? modifier)
    {
        EditModifierName = modifier?.Name ?? "";
        EditModifierDescription = modifier?.Description ?? "";
        OnPropertyChanged(nameof(IsEditingModifier));
    }

    private void LoadEditedSuperAlias(TagSuperAliasDTO? alias)
    {
        EditSuperAlias = alias?.Alias ?? "";
        EditSuperAliasExpanded = alias?.Expanded ?? "";
        OnPropertyChanged(nameof(IsEditingSuperAlias));
    }
}
