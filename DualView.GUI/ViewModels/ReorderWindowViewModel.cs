using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ReorderWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ReorderWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;

    private long collectionId;

    public ReorderWindowViewModel()
    {
        // Design time
    }

    [ActivatorUtilitiesConstructor]
    public ReorderWindowViewModel(ILogger<ReorderWindowViewModel> logger,
        IClientDatabaseService databaseService, IWindowService windowService,
        IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;
    }

    public ObservableCollection<MediaViewerViewModel> MainList { get; } = new();
    public ObservableCollection<MediaViewerViewModel> Workspace { get; } = new();

    public async Task Initialize(long id)
    {
        collectionId = id;
        await LoadItems();
    }

    private async Task LoadItems()
    {
        if (databaseService == null) return;

        try
        {
            // Load all items from collection (unpaged for reordering)
            var items = await databaseService.GetCollectionContents(collectionId);

            MainList.Clear();
            foreach (var item in items)
            {
                var vm = new MediaViewerViewModel(logger!, windowService!)
                {
                    Name = item.OriginalFileName,
                    MediaToShow = new Models.ServerMediaSource(new ConfiguredMediaInfo(item), serviceProvider!),
                    ShowingThumbnail = true,
                    AllowSelection = true,
                };
                MainList.Add(vm);
            }
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load items for reorder");
        }
    }

    public void MoveToWorkspace()
    {
        var selected = MainList.Where(i => i.Selected).ToList();
        foreach (var item in selected)
        {
            MainList.Remove(item);
            Workspace.Add(item);
            item.Selected = false;
        }
    }

    public void MoveFromWorkspace()
    {
        var selected = Workspace.Where(i => i.Selected).ToList();
        foreach (var item in selected)
        {
            Workspace.Remove(item);
            MainList.Add(item);
            item.Selected = false;
        }
    }

    public async Task Apply()
    {
        if (databaseService == null) return;

        try
        {
            // Save new order to database
            var newOrder = MainList
                .Select(i => i.MediaToShow)
                .OfType<Models.ServerMediaSource>()
                .Select(s => s.ServerId)
                .ToList();

            await databaseService.ReorderCollection(collectionId, newOrder);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to apply new order", e);
        }
    }

    public void Dispose()
    {
        foreach (var viewer in MainList)
        {
            viewer.Dispose();
        }

        MainList.Clear();

        foreach (var viewer in Workspace)
        {
            viewer.Dispose();
        }

        Workspace.Clear();
    }
}
