using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DualView.GUI.Services;
using DualView.GUI.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Requests;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualView.GUI.ViewModels;

public class ImportWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ImportWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly ILogger<FolderPickerViewModel>? folderPickerLogger;
    private readonly IServiceProvider? serviceProvider;

    public ImportWindowViewModel()
    {
        // Show some test items
        RecentNames.Add(new RecentImportSectionViewModel("Test 1", _ => { }));
        RecentNames.Add(new RecentImportSectionViewModel("Test 2", _ => { }));
        RecentNames.Add(new RecentImportSectionViewModel("Something else", _ => { }));

        Sections.Add(new ImportSectionViewModel(new UploadSectionDTO
        {
            Name = "Test import", RemoveAfterImport = true, Selected = true, TargetFolderId = 1,
        }, null!, null, null, null, null));

        Hamburger = new HamburgerMenuViewModel();

        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public ImportWindowViewModel(ILogger<ImportWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService databaseService, IBackendStatusService backendStatusService,
        ILogger<FolderPickerViewModel> folderPickerLogger, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.databaseService = databaseService;
        this.folderPickerLogger = folderPickerLogger;
        this.serviceProvider = serviceProvider;
        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        _ = ReloadAsync();
    }

    public ObservableCollection<ImportSectionViewModel> Sections { get; } = new();
    public ObservableCollection<RecentImportSectionViewModel> RecentNames { get; } = new();
    public HamburgerMenuViewModel Hamburger { get; }

    public string SearchText
    {
        get;
        set => SetProperty(ref field, value);
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

    public bool TargetNewSection
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value)
            {
                try
                {
                    _ = databaseService?.SetUploadSectionActiveAsync(null);
                }
                catch (Exception e)
                {
                    windowService?.ShowErrorWindow("Failed to set upload section active", e);
                }
            }
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
            var sections = await databaseService.GetUploadSectionsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var oldSection in Sections)
                    oldSection.Dispose();
                Sections.Clear();
                RecentNames.Clear();
                foreach (var section in sections)
                {
                    Sections.Add(new ImportSectionViewModel(section, databaseService, logger, windowService,
                        folderPickerLogger, serviceProvider));
                    if (!string.IsNullOrWhiteSpace(section.Name) &&
                        RecentNames.All(item => !item.Name.Equals(section.Name, StringComparison.Ordinal)))
                    {
                        RecentNames.Add(new RecentImportSectionViewModel(section.Name,
                            name => _ = ActivateOrCreateAsync(name)));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load import sections");
        }
    }

    public void Dispose()
    {
        foreach (var section in Sections)
            section.Dispose();
        Hamburger.Dispose();
    }

    private void SelectTab(int tab)
    {
        ImportTabSelected = tab == 0;
        ScanTabSelected = tab == 1;
        StatusTabSelected = tab == 2;
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

public sealed class ImportSectionViewModel : ViewModelBase, IDisposable
{
    private readonly IClientDatabaseService databaseService;
    private readonly ILogger? logger;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;
    private readonly long id;

    public ImportSectionViewModel(UploadSectionDTO section, IClientDatabaseService databaseService,
        ILogger? logger, IWindowService? windowService, ILogger<FolderPickerViewModel>? folderPickerLogger,
        IServiceProvider? serviceProvider)
    {
        this.databaseService = databaseService;
        this.logger = logger;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;

        id = section.Id;
        Name = section.Name;
        KeepEvenWhenEmpty = section.KeepTarget;
        RemoveAfterImport = section.RemoveAfterImport;
        IsActive = section.Selected;
        TargetFolderId = section.TargetFolderId;

        // If one service is given, assume all are available
        FolderPicker = folderPickerLogger != null && windowService != null
            ? new FolderPickerViewModel(folderPickerLogger, databaseService, windowService, serviceProvider!)
            : new FolderPickerViewModel();
        FolderPicker.PropertyChanged += OnFolderPickerPropertyChanged;
        _ = InitializeFolderPathAsync(section.TargetFolderId);

        foreach (var media in section.Media)
        {
            var viewer = new MediaViewerViewModel(logger ?? NullLogger.Instance, windowService!)
            {
                Name = media.OriginalFileName,
                MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(media),
                    serviceProvider ?? Program.ServiceProvider!),
                ShowingThumbnail = true,
                AllowSelection = true,
                Selected = false,
            };
            Media.Add(viewer);
            viewer.OnSelectionChanged += OnMediaSelectionChanged;
        }
    }

    public ObservableCollection<MediaViewerViewModel> Media { get; } = new();
    public FolderPickerViewModel FolderPicker { get; }
    public bool CollectionTabSelected { get; set; } = true;
    public bool ImagesTabSelected { get; set; }
    public bool IsCollapsed { get; set; }

    public string Name { get; set; }

    public long TargetFolderId { get; set; }

    public bool KeepEvenWhenEmpty { get; set; }
    public bool RemoveAfterImport { get; set; }
    public bool IsActive { get; set; }
    public int ImageCount => Media.Count;
    public int SelectedCount => Media.Count(item => item.Selected);

    public void SelectCollectionTab()
    {
        CollectionTabSelected = true;
        ImagesTabSelected = false;
        IsCollapsed = false;
        OnPropertyChanged();
    }

    public void SelectImagesTab()
    {
        CollectionTabSelected = false;
        ImagesTabSelected = true;
        IsCollapsed = false;
        OnPropertyChanged();
    }

    public void Collapse()
    {
        IsCollapsed = true;
        OnPropertyChanged();
    }

    public async Task SetActiveAsync()
    {
        await databaseService.SetUploadSectionActiveAsync(IsActive ? id : null);
    }

    // TODO: this should automatically save any changes after like a second (or if import is pressed immediately before doing the import)
    // This is probably needed just for the name change text box
    public async Task SaveAsync()
    {
        await databaseService.SaveUploadSectionAsync(new UploadSectionDTO
        {
            Id = id, Name = Name.Trim(), KeepTarget = KeepEvenWhenEmpty, RemoveAfterImport = RemoveAfterImport,
            TargetFolderId = TargetFolderId,
        });
    }

    public async Task ImportAsync()
    {
        var selected = Media.Where(item => item.Selected)
            .Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList();
        await databaseService.ImportUploadSectionAsync(id, selected.Count == 0 ? null : selected);

        // TODO: we need a signal R message to update the UI
    }

    public async Task RemoveSelectedAsync()
    {
        var selected = Media.Where(item => item.Selected).ToList();
        await databaseService.RemoveMediaFromUploadSectionAsync(id,
            selected.Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList());
        foreach (var item in selected)
        {
            item.OnSelectionChanged -= OnMediaSelectionChanged;
            item.Dispose();
            Media.Remove(item);
        }

        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void Dispose()
    {
        foreach (var media in Media)
        {
            media.OnSelectionChanged -= OnMediaSelectionChanged;
            media.Dispose();
        }
        FolderPicker.PropertyChanged -= OnFolderPickerPropertyChanged;
        FolderPicker.Dispose();
    }

    private async Task InitializeFolderPathAsync(long folderId)
    {
        if (folderId == MediaFolderInfo.RootFolderId)
        {
            FolderPicker.SelectedPath = "/";
            return;
        }

        try
        {
            FolderPicker.SelectedPath = await databaseService.GetMediaFolderPath(folderId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to resolve import target folder {FolderId}", folderId);
        }
    }

    private async void OnFolderPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderPickerViewModel.SelectedPath))
            return;

        var folder = await databaseService.GetMediaFolderFromPathAsync(FolderPicker.SelectedPath);
        if (folder != null)
        {
            TargetFolderId = folder.Id;
            OnPropertyChanged(nameof(TargetFolderId));

            // TODO: trigger backend save immediately
        }
    }

    private void OnMediaSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedCount));
    }
}
