using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class MediaPickerWindowViewModel : SideTabbedTreeViewBase
{
    private readonly ISignalRService? signalRService;
    private readonly IServiceProvider? serviceProvider;

    public MediaPickerWindowViewModel()
    {
    }

    [ActivatorUtilitiesConstructor]
    public MediaPickerWindowViewModel(ILogger<MediaPickerWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, ISignalRService signalRService,
        IBackendStatusService backendStatusService, IServiceProvider serviceProvider) :
        base(logger, windowService, clientDatabaseService, backendStatusService)
    {
        this.signalRService = signalRService;
        this.serviceProvider = serviceProvider;

        signalRService.OnMediaFoldersUpdated += ReloadFolderTree;
        signalRService.OnMediaFolderContentsUpdated += CheckFolderContentRefreshNotice;

        SortColumn = FolderSortColumn.DateCreated;
        SortDirection = SortDirection.Descending;

        InitializeMenu();
        ReloadFolderTree();
    }

    public ObservableCollection<MediaViewerViewModel> FolderItems { get; } = new();

    public FolderSortColumn[] SortColumnOptions { get; } =
        [FolderSortColumn.Name, FolderSortColumn.DateCreated, FolderSortColumn.DateModified];

    public bool MultiSelect
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ObservableCollection<ConfiguredMediaInfo> SelectedMedias { get; } = new();

    // Client-side search text for filtering the current page
    // TODO: implementing a server-side search would be very nice here
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // Reset to the first page on search change and refresh
                if (ItemPage != 1)
                    ItemPage = 1;
                RefreshFolderItems();
            }
        }
    } = string.Empty;

    public ConfiguredMediaInfo? SelectedMedia
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool WantsToClose
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public ConfiguredMediaDTO? Result { get; private set; }

    public List<ConfiguredMediaDTO>? ResultMultiple { get; private set; }

    public bool CanConfirm => MultiSelect ? SelectedMedias.Count > 0 : SelectedMedia != null;

    public void Confirm()
    {
        if (ClientDatabaseService == null)
            return;

        // Single-select path
        if (!MultiSelect)
        {
            if (SelectedMedia == null)
                return;

            _ = Task.Run(async void () =>
            {
                try
                {
                    var fullMedia = await ClientDatabaseService.GetConfiguredMediaAsync(SelectedMedia.Id);

                    Result = fullMedia ?? throw new Exception("Media not found by ID");
                    Dispatcher.UIThread.Post(() => { WantsToClose = true; });
                }
                catch (Exception e)
                {
                    WindowService?.ShowErrorWindow("Failed to get full media info after selection", e);
                }
            });
            return;
        }

        // Multi-select path
        if (SelectedMedias.Count == 0)
            return;

        _ = Task.Run(async void () =>
        {
            try
            {
                var list = new List<ConfiguredMediaDTO>(SelectedMedias.Count);
                foreach (var info in SelectedMedias)
                {
                    var full = await ClientDatabaseService.GetConfiguredMediaAsync(info.Id) ??
                               throw new Exception("Failed to retrieve info for media that was picked");

                    list.Add(full);
                }

                ResultMultiple = list;

                Dispatcher.UIThread.Post(() => { WantsToClose = true; });
            }
            catch (Exception e)
            {
                WindowService?.ShowErrorWindow("Failed to get full media info after selection", e);
            }
        });
    }

    public void Cancel()
    {
        SelectedMedia = null;
        Result = null;
        ResultMultiple = null;
        SelectedMedias.Clear();
        WantsToClose = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (signalRService != null)
            {
                signalRService.OnMediaFoldersUpdated -= ReloadFolderTree;
                signalRService.OnMediaFolderContentsUpdated -= CheckFolderContentRefreshNotice;
            }

            foreach (var folderItem in FolderItems)
            {
                folderItem.Dispose();
            }

            FolderItems.Clear();
        }

        base.Dispose(disposing);
    }

    protected override void AddDerivedMenuItems()
    {
    }

    protected override Task CreateFolderAsync(string name, long? parentId)
    {
        // As the user couldn't add media to a folder, it's not useful if this is called
        throw new NotSupportedException("It doesn't make much sense to create a folder in the media picker window");
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
            var (content, maxPages) = await ClientDatabaseService.GetMediaFolderContents(
                folder.ServerId, ItemPage - 1,
                PageSize, SortColumn, SortDirection);

            Dispatcher.UIThread.Post(() =>
            {
                // Show at least one page even if no items
                MaxPages = Math.Max(maxPages, 1);

                if (CheckTooHighPageNumber(maxPages))
                    return;

                foreach (var folderImage in FolderItems)
                {
                    folderImage.Dispose();
                }

                FolderItems.Clear();

                IEnumerable<ConfiguredMediaInfo> filtered = content;

                var filter = SearchText.Trim();
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    var f = filter.ToLowerInvariant();
                    filtered = filtered.Where(i =>
                        !string.IsNullOrEmpty(i.Name) && i.Name.ToLowerInvariant().Contains(f)
                    );
                }

                foreach (var item in filtered)
                {
                    var mediaViewModel = new MediaViewerViewModel(Logger!, WindowService!)
                    {
                        Name = item.Name,
                        MediaToShow = new ServerMediaSource(item, serviceProvider!),
                        ShowingThumbnail = true,
                        AllowSelection = true,
                    };

                    // This captures the item so needs to be here to capture a local
                    void OnMediaSelected(object? sender, EventArgs _)
                    {
                        if (sender is MediaViewerViewModel { Selected: true } viewer)
                        {
                            if (MultiSelect)
                            {
                                if (!SelectedMedias.Contains(item))
                                    SelectedMedias.Add(item);
                                OnPropertyChanged(nameof(CanConfirm));
                            }
                            else
                            {
                                SelectedMedia = item;

                                // Unselect others
                                foreach (var folderItem in FolderItems)
                                {
                                    if (folderItem != viewer)
                                    {
                                        folderItem.Selected = false;
                                    }
                                }

                                OnPropertyChanged(nameof(CanConfirm));
                            }
                        }
                        else if (sender is MediaViewerViewModel { Selected: false })
                        {
                            if (MultiSelect)
                            {
                                SelectedMedias.Remove(item);
                                OnPropertyChanged(nameof(CanConfirm));
                            }
                        }
                    }

                    mediaViewModel.OnSelectionChanged += OnMediaSelected;

                    FolderItems.Add(mediaViewModel);
                }
            });
        }
        catch (Exception e)
        {
            WindowService?.ShowErrorWindow("Failed to refresh media data", e);
        }
    }

    protected override void OnTooHighPageNumberCorrected()
    {
        foreach (var folderImage in FolderItems)
        {
            folderImage.Dispose();
        }

        FolderItems.Clear();
    }

    protected override Task PerformFolderDeleteAsync(long folderId)
    {
        throw new NotImplementedException();
    }
}
