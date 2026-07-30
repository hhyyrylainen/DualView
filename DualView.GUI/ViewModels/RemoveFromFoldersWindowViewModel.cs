using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class RemoveFromFoldersWindowViewModel : ViewModelBase
{
    private readonly ILogger<RemoveFromFoldersWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;

    private long? collectionId;
    private long? folderId;

    public RemoveFromFoldersWindowViewModel()
    {
        // Design time
        Folders.Add(new FolderEntry("/Root/Sub", 1, true));
    }

    [ActivatorUtilitiesConstructor]
    public RemoveFromFoldersWindowViewModel(ILogger<RemoveFromFoldersWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
    }

    public ObservableCollection<FolderEntry> Folders { get; } = new();

    public string ItemName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public async Task Initialize(CollectionDTO collection)
    {
        collectionId = collection.Id;
        ItemName = collection.Name;
        await LoadFolders();
    }

    public async Task Initialize(MediaFolderDTO folder)
    {
        folderId = folder.Id;
        ItemName = folder.Name;
        await LoadFolders();
    }

    private async Task LoadFolders()
    {
        if (databaseService == null) return;

        try
        {
            List<FolderPathDTO> folderPaths = new();
            if (collectionId.HasValue)
            {
                folderPaths = await databaseService.GetCollectionFolderPaths(collectionId.Value);
            }
            else if (folderId.HasValue)
            {
                folderPaths = await databaseService.GetFolderParentFolderPaths(folderId.Value);
            }

            Folders.Clear();
            foreach (var entry in folderPaths)
            {
                Folders.Add(new FolderEntry(entry.Path, entry.Id, true));
            }
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load folders for removal");
        }
    }

    public async Task Apply()
    {
        if (databaseService == null) return;

        try
        {
            var toRemove = Folders.Where(f => !f.Keep).ToList();
            foreach (var entry in toRemove)
            {
                if (collectionId.HasValue)
                {
                    await databaseService.RemoveCollectionFromFolder(collectionId.Value, entry.FolderId);
                }
                else if (folderId.HasValue)
                {
                    await databaseService.RemoveFolderFromFolder(folderId.Value, entry.FolderId);
                }
            }
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to remove from folders", e);
        }
    }

    public class FolderEntry(string path, long id, bool keep) : ViewModelBase
    {
        public string Path { get; } = path;
        public long FolderId { get; } = id;
        public bool Keep { get; set; } = keep;
    }
}
