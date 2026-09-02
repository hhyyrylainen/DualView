using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public class ImportWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ImportWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly ILogger<FolderPickerViewModel>? folderPickerLogger;
    private readonly IServiceProvider? serviceProvider;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;
    private List<RecentImportSectionDTO> loadedRecentSections = new();
    private List<MissingTagDTO> loadedMissingTags = new();

    public ImportWindowViewModel()
    {
        // Show some test items
        RecentNames.Add(new RecentImportSectionViewModel("Test 1", _ => { }));
        RecentNames.Add(new RecentImportSectionViewModel("Test 2", _ => { }));
        RecentNames.Add(new RecentImportSectionViewModel("Something else", _ => { }));

        Sections.Add(new ImportSectionViewModel());

        Hamburger = new HamburgerMenuViewModel();

        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public ImportWindowViewModel(ILogger<ImportWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService databaseService, IBackendStatusService backendStatusService,
        ILogger<FolderPickerViewModel> folderPickerLogger, IServiceProvider serviceProvider,
        ISignalRService signalRService, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.databaseService = databaseService;
        this.folderPickerLogger = folderPickerLogger;
        this.serviceProvider = serviceProvider;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;
        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        signalRService.OnUploadSectionsUpdated += OnUploadSectionsUpdated;
        signalRService.OnUploadSectionActiveChanged += OnUploadSectionActiveChanged;
        signalRService.OnMissingTagsUpdated += OnMissingTagsUpdated;
        _ = ReloadAsync();
    }

    public ObservableCollection<ImportSectionViewModel> Sections { get; } = new();
    public ObservableCollection<RecentImportSectionViewModel> RecentNames { get; } = new();
    public HamburgerMenuViewModel Hamburger { get; }

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                RefreshRecentNames();
        }
    } = string.Empty;

    public bool SidePanelOpen
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ImportTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ScanTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool StatusTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool MissingTagsTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ObservableCollection<MissingTagViewModel> MissingTags { get; } = new();

    public string MissingTagsHeader => $"Missing Tags ({MissingTags.Count})";

    public bool SortMissingTagsByFrequency
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                UpdateMissingTags();
        }
    }

    public bool TargetNewSection
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value)
                _ = SetTargetNewSectionAsync();
        }
    }

    public void ToggleSidePanel()
    {
        SidePanelOpen = !SidePanelOpen;
    }

    public void SelectImportTab()
    {
        SelectTab(0);
    }

    public void SelectScanTab()
    {
        SelectTab(1);
    }

    public void SelectStatusTab()
    {
        SelectTab(2);
    }

    public void SelectMissingTagsTab()
    {
        SelectTab(3);
    }

    private async Task IgnoreMissingTagAsync(string tag)
    {
        if (databaseService == null)
            return;
        await databaseService.AddIgnoredTagAsync(tag);
        await ReloadMissingTagsAsync();
    }

    public async Task ResetIgnoredTagsAsync()
    {
        if (databaseService == null)
            return;
        await databaseService.ClearIgnoredTagsAsync();
        await ReloadMissingTagsAsync();
    }

    public async Task ClearCurrentMissingTagDetectionsAsync()
    {
        if (backendAPI == null)
            return;

        await backendAPI.ClearCurrentMissingTagDetections();
        await ReloadMissingTagsAsync();
    }

    private void CreateMissingTag(string tag)
    {
        var manager = windowService?.ShowSingletonWindow<TagManagerWindowViewModel>();
        if (manager != null)
            manager.BeginNewTag(tag);
    }

    public async Task ActivateOrCreateAsync(string name)
    {
        if (databaseService == null)
            return;

        name = name.Trim();
        if (name.Length == 0)
            return;

        try
        {
            var section = await databaseService.GetOrCreateUploadSectionAsync(name);
            await databaseService.SetUploadSectionActiveAsync(section.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to activate import section {Name}", name);
        }
    }

    public void SearchOrCreate() => _ = ActivateOrCreateAsync(SearchText);

    public void ActivateOrCreate(object? value)
    {
        if (value is string name)
            _ = ActivateOrCreateAsync(name);
    }

    public void OpenUploadWindow() => windowService?.ShowWindow<UploadWindowViewModel>();

    public async Task ReloadAsync()
    {
        if (databaseService == null)
            return;

        try
        {
            var sectionsTask = databaseService.GetUploadSectionsAsync();
            var recentSectionsTask = databaseService.GetRecentImportSectionsAsync();
            var missingTagsTask = databaseService.GetMissingTagsAsync();
            await Task.WhenAll(sectionsTask, recentSectionsTask, missingTagsTask);
            var sections = await sectionsTask;
            var recentSections = await recentSectionsTask;
            var missingTags = await missingTagsTask;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                loadedRecentSections = recentSections;
                loadedMissingTags = missingTags;
                UpdateMissingTags();
                var existingSections = Sections.ToDictionary(section => section.Id);
                var refreshedSections = sections.Select(section =>
                    existingSections.TryGetValue(section.Id, out var existingSection)
                        ? existingSection
                        : new ImportSectionViewModel(section, databaseService, logger, windowService,
                            folderPickerLogger, serviceProvider, signalRService, backendAPI)).ToList();

                foreach (var removedSection in existingSections.Values.Where(oldSection =>
                             refreshedSections.All(section => !ReferenceEquals(section, oldSection))))
                {
                    removedSection.Dispose();
                }

                for (var index = 0; index < refreshedSections.Count; ++index)
                {
                    var section = refreshedSections[index];
                    if (index < Sections.Count && ReferenceEquals(Sections[index], section))
                    {
                        section.UpdateFromServer(sections[index]);
                        continue;
                    }

                    var currentIndex = Sections.IndexOf(section);
                    if (currentIndex >= 0)
                        Sections.Move(currentIndex, index);
                    else
                        Sections.Insert(index, section);

                    section.UpdateFromServer(sections[index]);
                }

                while (Sections.Count > refreshedSections.Count)
                    Sections.RemoveAt(Sections.Count - 1);

                RefreshRecentNames();
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load import sections");
        }
    }

    public void Dispose()
    {
        signalRService?.OnUploadSectionsUpdated -= OnUploadSectionsUpdated;
        signalRService?.OnUploadSectionActiveChanged -= OnUploadSectionActiveChanged;
        signalRService?.OnMissingTagsUpdated -= OnMissingTagsUpdated;
        foreach (var section in Sections)
            section.Dispose();
        Hamburger.Dispose();
    }

    private async Task SetTargetNewSectionAsync()
    {
        try
        {
            if (databaseService != null)
                await databaseService.SetUploadSectionActiveAsync(null);
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to set upload section active", ex);
        }
    }

    private void SelectTab(int tab)
    {
        ImportTabSelected = tab == 0;
        ScanTabSelected = tab == 1;
        StatusTabSelected = tab == 2;
        MissingTagsTabSelected = tab == 3;
    }

    private void OnUploadSectionsUpdated()
    {
        _ = ReloadAsync();
    }

    private void OnUploadSectionActiveChanged(long? activeSectionId)
    {
        if (activeSectionId.HasValue)
            Dispatcher.UIThread.Post(() => TargetNewSection = false);
    }

    private void OnMissingTagsUpdated()
    {
        _ = ReloadMissingTagsAsync();
    }

    private async Task ReloadMissingTagsAsync()
    {
        if (databaseService == null)
            return;
        var missingTags = await databaseService.GetMissingTagsAsync();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            loadedMissingTags = missingTags;
            UpdateMissingTags();
        });
    }

    private void UpdateMissingTags()
    {
        MissingTags.Clear();
        var groupedMissingTags = loadedMissingTags
            .GroupBy(tag => tag.Tag, StringComparer.Ordinal)
            .Select(group => (Tag: group.First(), Count: group.Count()));
        if (SortMissingTagsByFrequency)
            groupedMissingTags = groupedMissingTags.OrderByDescending(group => group.Count);

        foreach (var missingTag in groupedMissingTags)
        {
            MissingTags.Add(new MissingTagViewModel(missingTag.Tag, missingTag.Count,
                IgnoreMissingTagAsync, CreateMissingTag));
        }

        OnPropertyChanged(nameof(MissingTagsHeader));
    }

    private void RefreshRecentNames()
    {
        var searchText = SearchText.Trim();
        var names = loadedRecentSections
            .Where(section => searchText.Length == 0 ||
                              section.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
            .Select(section => section.Name)
            .ToList();

        RecentNames.Clear();
        foreach (var name in names)
        {
            RecentNames.Add(new RecentImportSectionViewModel(name,
                selectedName => _ = ActivateOrCreateAsync(selectedName)));
        }
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        // TODO: add some items here like "Import All Sections"
        /*Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Import All Sections", Command = new RelayCommand(StartImportAll) });*/

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}

public sealed class RecentImportSectionViewModel(string name, Action<string> activate)
{
    public string Name { get; } = name;

    public void Activate()
    {
        activate(Name);
    }
}

public sealed class MissingTagViewModel
{
    private readonly Func<string, Task> ignore;
    private readonly Action<string> create;
    private readonly string originalTag;

    public MissingTagViewModel(MissingTagDTO tag, int count, Func<string, Task> ignore, Action<string> create)
    {
        originalTag = tag.Tag;
        Tag = count > 1 ? $"{tag.Tag} ({count})" : tag.Tag;
        Target = tag.Target.ToString();
        TargetId = tag.TargetId;
        this.ignore = ignore;
        this.create = create;
    }

    public string Tag { get; }
    public string Target { get; }
    public long TargetId { get; }

    public void Ignore() => _ = ignore(originalTag);

    public void Create() => create(originalTag);
}
