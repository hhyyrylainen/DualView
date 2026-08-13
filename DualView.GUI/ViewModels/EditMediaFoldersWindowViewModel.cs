using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
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
    private long? collectionId;
    private long? folderId;

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

    public bool AddToRoot
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string ManagedItemType
    {
        get;
        private set => SetProperty(ref field, value);
    } = "media";

    public string ManagedItemName
    {
        get;
        private set => SetProperty(ref field, value);
    } = "media";

    public string ExistingFoldersHeading => $"Current folders for this {ManagedItemType}:";
    public string AddFolderHeading => $"Choose a folder to add this {ManagedItemType} to:";

    public void Initialize(long configuredMediaId)
    {
        mediaConfigId = configuredMediaId;
        collectionId = null;
        folderId = null;
        ManagedItemType = "media";
        ManagedItemName = "media";
        NotifyManagedItemTextChanged();
        _ = Task.Run(LoadFolders);
    }

    public void Initialize(IConfiguredMediaInfo item)
    {
        mediaConfigId = 0;
        collectionId = item.IsCollection ? item.Id : null;
        folderId = item.IsFolder ? item.Id : null;
        ManagedItemType = item.IsCollection ? "collection" : "folder";
        ManagedItemName = item.Name;
        NotifyManagedItemTextChanged();
        _ = Task.Run(LoadFolders);
    }

    private async Task LoadFolders()
    {
        if (clientDatabaseService == null || mediaConfigId == 0)
            return;

        try
        {
            ExistingFolders.Clear();
            if (collectionId.HasValue)
            {
                var paths = await clientDatabaseService.GetCollectionFolderPaths(collectionId.Value);
                AddExistingFolders(paths.Select(path => path.Path));
            }
            else if (folderId.HasValue)
            {
                var paths = await clientDatabaseService.GetFolderParentFolderPaths(folderId.Value);
                AddExistingFolders(paths.Select(path => path.Path));
            }
            else
            {
                var info = await clientDatabaseService.GetConfiguredMediaFoldersAsync(mediaConfigId);
                if (!string.IsNullOrWhiteSpace(info.PrimaryFolder))
                    ExistingFolders.Add(new FolderItem { Name = info.PrimaryFolder, Selected = true });

                if (info.SecondaryFolders != null)
                    AddExistingFolders(info.SecondaryFolders);
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
                if (collectionId.HasValue || folderId.HasValue)
                {
                    var folder = await clientDatabaseService.GetMediaFolderFromPathAsync(item.Name);
                    if (folder == null)
                        continue;

                    if (collectionId.HasValue)
                    {
                        await clientDatabaseService.RemoveCollectionFromFolder(collectionId.Value, folder.Id);
                    }
                    else
                    {
                        await clientDatabaseService.RemoveFolderFromFolder(folderId!.Value, folder.Id);
                    }
                }
                else
                {
                    await clientDatabaseService.RemoveMediaFromFolder(mediaConfigId, item.Name);
                }
            }

            // Add new folder if provided
            if (AddToRoot)
            {
                await AddToFolderAsync("/");
            }
            else if (!string.IsNullOrWhiteSpace(NewFolderPath) && NewFolderPath != "/")
            {
                await AddToFolderAsync(NewFolderPath.Trim());
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

    private async Task AddToFolderAsync(string path)
    {
        if (collectionId.HasValue || folderId.HasValue)
        {
            var folder = path == "/"
                ? new MediaFolderDTO("Root") { Id = MediaFolderInfo.RootFolderId }
                : await clientDatabaseService!.GetMediaFolderFromPathAsync(path);
            if (folder == null)
                throw new InvalidOperationException("The selected folder could not be found.");

            if (collectionId.HasValue)
                await clientDatabaseService!.AddCollectionToFolder(collectionId.Value, folder.Id);
            else
                await clientDatabaseService!.AddFolderToFolder(folderId!.Value, folder.Id);
        }
        else
        {
            await clientDatabaseService!.AddMediaToFolder(mediaConfigId, path, path == "/");
        }
    }

    private void AddExistingFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            ExistingFolders.Add(new FolderItem
            {
                Name = folder,
                Selected = true,
            });
        }
    }

    private void NotifyManagedItemTextChanged()
    {
        OnPropertyChanged(nameof(ExistingFoldersHeading));
        OnPropertyChanged(nameof(AddFolderHeading));
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
