using System;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class AddToFolderWindowViewModel : ViewModelBase
{
    private readonly ILogger<AddToFolderWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;

    private long? collectionId;
    private long? folderId;

    public AddToFolderWindowViewModel()
    {
        // Design time
        FolderSelector = new FolderSelectorViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public AddToFolderWindowViewModel(ILogger<AddToFolderWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;

        FolderSelector = new FolderSelectorViewModel(logger, databaseService, windowService);
    }

    public FolderSelectorViewModel FolderSelector { get; }

    public string ItemName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public void Initialize(CollectionDTO collection)
    {
        collectionId = collection.Id;
        ItemName = collection.Name;
    }

    public void Initialize(MediaFolderDTO folder)
    {
        folderId = folder.Id;
        ItemName = folder.Name;
    }

    public async Task Apply()
    {
        if (databaseService == null) return;

        try
        {
            var targetFolderId = FolderSelector.SelectedFolderNode?.ServerId ?? MediaFolderInfo.RootFolderId;

            if (collectionId.HasValue)
            {
                await databaseService.AddCollectionToFolder(collectionId.Value, targetFolderId);
            }
            else if (folderId.HasValue)
            {
                await databaseService.AddFolderToFolder(folderId.Value, targetFolderId);
            }
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to add to folder", e);
        }
    }
}
