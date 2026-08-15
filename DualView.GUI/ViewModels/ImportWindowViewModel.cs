using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Services;
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