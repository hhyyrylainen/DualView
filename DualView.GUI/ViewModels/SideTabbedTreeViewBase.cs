using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public abstract class SideTabbedTreeViewBase : ViewModelBase, IDisposable
{
    protected readonly ILogger? Logger;
    protected readonly IWindowService? WindowService;
    protected readonly IClientDatabaseService? ClientDatabaseService;

    protected bool AutomaticallyAdjustingPaging;

    public event EventHandler? OnWantsToCreateFolder;

    protected SideTabbedTreeViewBase()
    {
        Hamburger = new HamburgerMenuViewModel();
    }

    protected SideTabbedTreeViewBase(ILogger logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, IBackendStatusService backendStatusService)
    {
        Logger = logger;
        WindowService = windowService;
        ClientDatabaseService = clientDatabaseService;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);
    }

    public bool SidePanelOpen
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public long? LastSelectedFolderId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ObservableCollection<FolderTreeNode> Folders { get; } = new();

    /// <summary>
    ///   Callback to request copying text to clipboard (needs to be hooked by the window code-behind to function)
    /// </summary>
    public Func<string, Task>? RequestCopyToClipboard
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(HasCopyPathAction));
        }
    }

    public bool HasCopyPathAction => RequestCopyToClipboard != null;

    public FolderTreeNode? SelectedFolderNode
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);

            if (value != null)
            {
                LastSelectedFolderId = value.ServerId;
                OnFolderSelectionChanged();
                RefreshFolderItems();
            }
        }
    }

    public int ItemPage
    {
        get;
        set
        {
            if (value == field)
                return;

            var old = value;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(CanNavigateForwards));
            OnPropertyChanged(nameof(CanNavigateBackwards));

            // Changing from 0 to 1 doesn't change anything
            if (old == 0 && value <= 1)
                return;

            // Otherwise the page changed so refresh
            Logger?.LogInformation("Page changed to {Page}", value);
            RefreshFolderItems();
        }
    } = 0;

    public int MaxPages
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(CanNavigateForwards));
            OnPropertyChanged(nameof(CanNavigateBackwards));
        }
    } = 0;

    public int PageSize
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            Logger?.LogInformation("Page size changed to {Size}", value);
            RefreshFolderItems();
        }
    } = 100;

    public bool CanNavigateForwards => ItemPage < MaxPages && MaxPages > 0;

    public bool CanNavigateBackwards => ItemPage > 1 && MaxPages > 0;

    public int[] PageSizeOptions { get; } = [25, 50, 100, 200, 500, 1000];

    public SortDirection SortDirection
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            Logger?.LogInformation("Page sort direction changed to {Direction}", value);
            RefreshFolderItems();
        }
    } = SortDirection.Descending;

    public SortDirection[] SortDirectionOptions { get; } = Enum.GetValues<SortDirection>();

    public FolderSortColumn SortColumn
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            Logger?.LogInformation("Page sort column changed to {Column}", value);
            RefreshFolderItems();
        }
    } = FolderSortColumn.DateCreated;

    public HamburgerMenuViewModel Hamburger { get; }

    public void ReloadFolderTree()
    {
        _ = LoadFolders();
    }

    public void OpenSidePanel()
    {
        SidePanelOpen = true;
    }

    public void OpenFolderCreation()
    {
        OnWantsToCreateFolder?.Invoke(this, EventArgs.Empty);
    }

    public void CloseSidePanel()
    {
        SidePanelOpen = false;
    }

    public void RefreshFolderItems()
    {
        if (AutomaticallyAdjustingPaging)
        {
            Logger?.LogDebug("Skipping refreshing folder items because we're currently adjusting paging");
            return;
        }

        _ = LoadFolderItems(true);
    }

    private async Task OnCopyPathRequested(FolderTreeNode originatingNode, FolderTreeNode _)
    {
        try
        {
            var path = FolderTreeNode.GetPath(Folders, originatingNode);
            if (string.IsNullOrWhiteSpace(path))
                return;

            // If not hooked, the menu should be disabled but to be safe
            if (RequestCopyToClipboard == null)
                return;

            await RequestCopyToClipboard.Invoke(path);
        }
        catch (Exception e)
        {
            WindowService?.ShowErrorWindow("Failed to copy folder path", e);
        }
    }

    public void DeleteSelectedFolder()
    {
        var serverId = SelectedFolderNode?.ServerId;
        if (serverId == null)
            return;

        _ = RequestFolderDelete(serverId.Value);
    }

    public void GoToPreviousPage()
    {
        ItemPage = Math.Max(1, ItemPage - 1);
    }

    public void GoToFirstPage()
    {
        ItemPage = 1;
    }

    public void GoBackwards5()
    {
        ItemPage = Math.Max(1, ItemPage - 5);
    }

    public void GoBackwards10()
    {
        ItemPage = Math.Max(1, ItemPage - 10);
    }

    public void GoToNextPage()
    {
        ItemPage = Math.Min(ItemPage + 1, MaxPages);
    }

    public void GoToLastPage()
    {
        ItemPage = Math.Max(1, MaxPages);
    }

    public void GoForward5()
    {
        ItemPage = Math.Min(MaxPages, ItemPage + 5);
    }

    public void GoForward10()
    {
        ItemPage = Math.Min(MaxPages, ItemPage + 10);
    }

    public async Task<bool> TryCreateNewFolderAsync(string? folderName, long? parentId,
        TextInputWindowViewModel errorReceiver)
    {
        if (string.IsNullOrWhiteSpace(folderName) || folderName.Length < 3)
        {
            errorReceiver.Error = "Name cannot be empty or less than 3 characters";
            return false;
        }

        if (folderName.Length > 500)
        {
            errorReceiver.Error = "That's a way too long name";
            return false;
        }

        if (ClientDatabaseService == null)
        {
            errorReceiver.Error = "Backend service not available";
            return false;
        }

        try
        {
            await CreateFolderAsync(folderName, parentId);
            return true;
        }
        catch (Exception e)
        {
            errorReceiver.Error = $"Failed to create folder (is the name already used in the folder?): {e.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        AddDerivedMenuItems();

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Refresh Folders", Command = new RelayCommand(ReloadFolderTree) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Refresh Current Items", Command = new RelayCommand(RefreshFolderItems) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, WindowService);
    }

    protected abstract void AddDerivedMenuItems();

    protected void CheckFolderContentRefreshNotice(long folderId)
    {
        if (folderId == SelectedFolderNode?.ServerId || folderId == LastSelectedFolderId)
        {
            Logger?.LogInformation("We got a folder refresh notice, doing a refresh now for {Id}", folderId);
            _ = LoadFolderItems(false);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hamburger.Dispose();
        }
    }

    protected abstract Task CreateFolderAsync(string name, long? parentId);
    protected abstract Task<IEnumerable<IFolderInfo>> LoadFolderInfoAsync();

    protected abstract Task LoadFolderItems(bool searchChanged);

    protected virtual void OnFolderSelectionChanged()
    {
        AutomaticallyAdjustingPaging = true;
        ItemPage = 1;
        MaxPages = 0;
        AutomaticallyAdjustingPaging = false;
    }

    protected bool CheckTooHighPageNumber(int maxPages)
    {
        if (ItemPage <= maxPages || maxPages <= 0)
            return false;

        Logger?.LogWarning("Fixing being at too high page number ({Current}) when page count is: {Count}",
            ItemPage, maxPages);

        OnTooHighPageNumberCorrected();

        Dispatcher.UIThread.Post(() => ItemPage = maxPages, DispatcherPriority.Background);
        return true;
    }

    protected abstract void OnTooHighPageNumberCorrected();

    protected virtual Task<bool> CanDeleteFolder(long serverId)
    {
        return Task.FromResult(true);
    }

    protected abstract Task PerformFolderDeleteAsync(long folderId);

    private async Task LoadFolders()
    {
        if (ClientDatabaseService == null)
            return;

        try
        {
            // As the tree doesn't really support getting child content when needed, we have to load *everything* into
            // memory at once
            var folders = await LoadFolderInfoAsync();

            var folderGroups = folders.GroupBy(f => f.ParentId).OrderBy(g => g.Key != null).ThenBy(g => g.Key).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                FolderTreeBuilder.HandleTree(Folders, folderGroups, (name, id) => new FolderTreeNode(name, id));

                // Attach root-level handlers so nodes can bubble operations up
                foreach (var root in Folders)
                {
                    root.OnCopyPath = OnCopyPathRequested;
                }
            });
        }
        catch (Exception e)
        {
            WindowService?.ShowErrorWindow("Failed to refresh tree data", e);
        }
    }

    private async Task RequestFolderDelete(long serverId)
    {
        try
        {
            if (!await CanDeleteFolder(serverId))
            {
                Logger?.LogInformation("Folder delete cancelled");
                return;
            }

            Logger?.LogInformation("Starting delete of folder {Id}", serverId);
            await PerformFolderDeleteAsync(serverId);

            Logger?.LogInformation("Folder deleted: {Id}", serverId);

            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedFolderNode?.ServerId == serverId)
                {
                    Logger?.LogInformation("Clearing selected folder because it was deleted");
                    SelectedFolderNode = null;
                    LastSelectedFolderId = null;
                }

                // A general refresh shouldn't be necessary as the server should notify of the change
            });
        }
        catch (Exception e)
        {
            WindowService?.ShowErrorWindow(
                "Failed to delete folder (is it fully empty with no even deleted items in it?)", e);
        }
    }
}
