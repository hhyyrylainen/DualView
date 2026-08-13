using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class EditMediaFoldersWindowViewModel : ViewModelBase
{
    private readonly ILogger<EditMediaFoldersWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;

    private long mediaConfigId;

    public EditMediaFoldersWindowViewModel()
    {
        FolderPicker = new FolderPickerViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public EditMediaFoldersWindowViewModel(ILogger<EditMediaFoldersWindowViewModel> logger,
        ILogger<FolderPickerViewModel> folderPickerLogger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
        FolderPicker =
            new FolderPickerViewModel(folderPickerLogger, clientDatabaseService, windowService, serviceProvider);
        FolderPicker.PropertyChanged += OnFolderPickerPropertyChanged;
    }

    public FolderPickerViewModel FolderPicker { get; }

    public ObservableCollection<FolderItem> ExistingFolders { get; } = new();

    public string NewFolderPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public void Initialize(long configuredMediaId)
    {
        mediaConfigId = configuredMediaId;
        _ = Task.Run(LoadFolders);
    }

    private async Task LoadFolders()
    {
        if (clientDatabaseService == null || mediaConfigId == 0)
            return;

        try
        {
            ExistingFolders.Clear();
            var info = await clientDatabaseService.GetConfiguredMediaFoldersAsync(mediaConfigId);
            if (!string.IsNullOrWhiteSpace(info.PrimaryFolder))
            {
                ExistingFolders.Add(new FolderItem
                {
                    Name = info.PrimaryFolder,
                    Selected = true,
                });
            }

            if (info.SecondaryFolders != null)
            {
                foreach (var folder in info.SecondaryFolders)
                {
                    ExistingFolders.Add(new FolderItem
                    {
                        Name = folder,
                        Selected = true,
                    });
                }
            }
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load media folders");
            windowService?.ShowErrorWindow("Failed to load media folders", e);
        }
    }

    public async Task<bool> Apply()
    {
        if (clientDatabaseService == null)
            return true;

        try
        {
            // Remove unchecked folders
            foreach (var item in ExistingFolders.Where(f => !f.Selected).ToList())
            {
                await clientDatabaseService.RemoveMediaFromFolder(mediaConfigId, item.Name);
            }

            // Add new folder if provided
            if (!string.IsNullOrWhiteSpace(NewFolderPath))
            {
                await clientDatabaseService.AddMediaToFolder(mediaConfigId, NewFolderPath.Trim(), true);
            }

            return true;
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to apply folder changes");
            windowService?.ShowErrorWindow("Failed to apply folder changes", e);
            return false;
        }
    }

    private void OnFolderPickerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderPickerViewModel.SelectedPath))
            NewFolderPath = FolderPicker.SelectedPath;
    }

    public class FolderItem : ObservableObject
    {
        public string Name { get; init; } = string.Empty;

        public bool Selected
        {
            get;
            set => SetProperty(ref field, value);
        } = true;
    }
}
