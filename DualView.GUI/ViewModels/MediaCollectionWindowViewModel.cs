using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class MediaCollectionWindowViewModel : SideTabbedTreeViewBase
{
    private readonly ISignalRService? signalRService;
    private readonly IServiceProvider? serviceProvider;

    // Design time constructor
    public MediaCollectionWindowViewModel()
    {
        Folders.Add(new FolderTreeNode("Test1", 1));

        Folders.Add(new FolderTreeNode("Sub test", 2, [
            new FolderTreeNode("Sub sub test", 3),
        ]));

        SelectedFolderNode = Folders[0];

        FolderImages.Add(new MediaViewerViewModel
        {
            Name = "Test",
        });
    }

    [ActivatorUtilitiesConstructor]
    public MediaCollectionWindowViewModel(ILogger<MediaCollectionWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, ISignalRService signalRService,
        IBackendStatusService backendStatusService, IServiceProvider serviceProvider) :
        base(logger, windowService, clientDatabaseService, backendStatusService)
    {
        this.signalRService = signalRService;
        this.serviceProvider = serviceProvider;

        signalRService.OnMediaFoldersUpdated += ReloadFolderTree;
        signalRService.OnMediaFolderContentsUpdated += CheckFolderContentRefreshNotice;

        InitializeMenu();
        ReloadFolderTree();
    }

    public ObservableCollection<MediaViewerViewModel> FolderImages { get; } = new();

    public FolderSortColumn[] SortColumnOptions { get; } =
        { FolderSortColumn.DateCreated, FolderSortColumn.Name, FolderSortColumn.DateModified };

    public void OpenImportWindow()
    {
        if (WindowService == null)
            return;

        var opened = WindowService.ShowSingletonWindow<ImportWindowViewModel>();
        if (opened != null)
        {
            if (SelectedFolderNode != null)
            {
                Logger?.LogInformation("Will try to open importer to current folder");
                var path = FolderTreeNode.GetPath(Folders, SelectedFolderNode);

                if (!string.IsNullOrWhiteSpace(path))
                {
                    opened.TargetImportPath = path;
                }
            }
        }
        else
        {
            Logger?.LogWarning("Can't open import to a specific folder");
        }
    }

    protected override void AddDerivedMenuItems()
    {
        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Import...", Command = new RelayCommand(OpenImportWindow) });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            if (signalRService != null)
            {
                signalRService.OnMediaFoldersUpdated -= ReloadFolderTree;
                signalRService.OnMediaFolderContentsUpdated -= CheckFolderContentRefreshNotice;
            }

            foreach (var folderImage in FolderImages)
            {
                folderImage.Dispose();
            }
        }
    }

    protected override Task CreateFolderAsync(string name, long? parentId)
    {
        return ClientDatabaseService!.CreateMediaFolder(name, parentId);
    }

    protected override async Task<IEnumerable<IFolderInfo>> LoadFolderInfoAsync()
    {
        return await ClientDatabaseService!.GetMediaFoldersAsync();
    }

    protected override async Task LoadFolderItems(bool searchChanged)
    {
        var folder = SelectedFolderNode;

        if (ClientDatabaseService == null || folder == null)
            return;

        try
        {
            // TODO: implementing a server-side search would be very nice here with name text
            var (content, maxPages) = await ClientDatabaseService.GetMediaFolderContents(folder.ServerId, ItemPage - 1,
                PageSize, SortColumn, SortDirection);

            Dispatcher.UIThread.Post(() =>
            {
                // Show at least one page even if no items
                MaxPages = Math.Max(maxPages, 1);

                if (CheckTooHighPageNumber(maxPages))
                    return;

                // TODO: smarter refresh when search didn't change so that we just got an update notice from the server
                // Dispose the existing to stop them playing
                foreach (var folderImage in FolderImages)
                {
                    folderImage.Dispose();
                }

                FolderImages.Clear();

                foreach (var item in content)
                {
                    FolderImages.Add(new MediaViewerViewModel(Logger!, WindowService!)
                    {
                        Name = item.Name,
                        MediaToShow = new ServerMediaSource(item, serviceProvider!),

                        MediaOpenResources = new ShowMediaInSeparateWindow(WindowService!),
                        ShowingThumbnail = true,
                    });
                }
            });
        }
        catch (Exception e)
        {
            WindowService?.ShowErrorWindow("Failed to refresh tree data", e);
        }
    }

    protected override void OnTooHighPageNumberCorrected()
    {
        FolderImages.Clear();
    }

    protected override Task PerformFolderDeleteAsync(long folderId)
    {
        // TODO: implement this
        throw new NotImplementedException();
    }
}
