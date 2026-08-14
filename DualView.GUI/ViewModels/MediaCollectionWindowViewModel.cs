using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public sealed class MediaCollectionWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<MediaCollectionWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;

    private readonly Dictionary<int, Vector> pageScrollOffsets = new();

    private long? collectionId;
    private int currentPage = 1;
    private int totalPages = 1;
    private int pageSize = 500;
    private string searchText = string.Empty;

    private Vector? pendingScrollOffsetRestore;
    private int scrollOffsetRestoreVersion;

    public MediaCollectionWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        CollectionItems.Add(new MediaViewerViewModel { Name = "Test item", AllowSelection = true });
        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public MediaCollectionWindowViewModel(ILogger<MediaCollectionWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService databaseService, IBackendStatusService backendStatusService,
        IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;
        Hamburger = new HamburgerMenuViewModel(backendStatusService);
        InitializeMenu();
    }

    public ObservableCollection<MediaViewerViewModel> CollectionItems { get; } = new();
    public HamburgerMenuViewModel Hamburger { get; }
    public int[] PageSizeOptions { get; } = [100, 200, 500, 1000, 2000];
    public CollectionSortColumn[] SortColumnOptions { get; } = Enum.GetValues<CollectionSortColumn>();
    public SortDirection[] SortDirectionOptions { get; } = Enum.GetValues<SortDirection>();

    public CollectionDTO? Collection
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(WindowCollectionTitle));
                OnPropertyChanged(nameof(CollectionCreatedAt));
                OnPropertyChanged(nameof(CollectionStatistics));
                OnPropertyChanged(nameof(IsPairedImageMode));
            }
        }
    }

    public string WindowCollectionTitle => "DualView - " + (Collection?.Name ?? "No collection selected");
    public DateTime? CollectionCreatedAt => Collection?.CreatedAt;
    public string CollectionStatistics => Collection == null ? "" : $"{CollectionItemCount} images";
    public int CollectionItemCount { get; private set; }

    public bool IsPairedImageMode
    {
        get => Collection?.ImageGroupSize == 2;
        set
        {
            if (Collection == null || value == IsPairedImageMode)
                return;

            _ = SetPairedImageMode(value);
        }
    }

    public string CollectionTags
    {
        get;
        set => SetProperty(ref field, value);
    } = "TODO: implement tag fetching";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ResetScrollPositionCache(true);
                _ = RefreshItems();
            }
        }
    }

    public int PageSize
    {
        get => pageSize;
        set
        {
            if (SetProperty(ref pageSize, value))
            {
                ResetScrollPositionCache(true);
                _ = RefreshItems();
            }
        }
    }

    public CollectionSortColumn SortColumn
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ResetScrollPositionCache(false);
                _ = RefreshItems();
            }
        }
    } = CollectionSortColumn.CollectionOrder;

    public SortDirection SortDirection
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ResetScrollPositionCache(false);
                _ = RefreshItems();
            }
        }
    } = SortDirection.Ascending;

    public bool IsReversed
    {
        get => SortDirection == SortDirection.Descending;
        set
        {
            var direction = value ? SortDirection.Descending : SortDirection.Ascending;
            if (direction != SortDirection)
                SortDirection = direction;
            OnPropertyChanged();
        }
    }

    public int CurrentPage
    {
        get => currentPage;
        set
        {
            var nextPage = Math.Clamp(value, 1, totalPages);
            if (nextPage == currentPage)
            {
                OnPropertyChanged(nameof(CanNavigateBackwards));
                OnPropertyChanged(nameof(CanNavigateForwards));
                return;
            }

            pendingScrollOffsetRestore = pageScrollOffsets.TryGetValue(nextPage, out var scrollOffset)
                ? scrollOffset
                : null;
            ++scrollOffsetRestoreVersion;
            if (SetProperty(ref currentPage, nextPage))
            {
                CollectionScrollOffset = new Vector(0, 0);
                _ = RefreshItems();
            }

            OnPropertyChanged(nameof(CanNavigateBackwards));
            OnPropertyChanged(nameof(CanNavigateForwards));
        }
    }

    public Vector CollectionScrollOffset
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                pageScrollOffsets[currentPage] = value;
        }
    }

    public int TotalPages
    {
        get => totalPages;
        private set
        {
            if (SetProperty(ref totalPages, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(CanNavigateBackwards));
                OnPropertyChanged(nameof(CanNavigateForwards));
            }
        }
    }

    public bool CanNavigateBackwards => CurrentPage > 1;
    public bool CanNavigateForwards => CurrentPage < TotalPages;
    public int SelectedCount => CollectionItems.Count(item => item.Selected);
    public event EventHandler? CloseRequested;

    public async Task Initialize(long id, string? fallbackName = null)
    {
        collectionId = id;
        pageScrollOffsets.Clear();
        pendingScrollOffsetRestore = null;
        ++scrollOffsetRestoreVersion;
        currentPage = 1;
        OnPropertyChanged(nameof(CurrentPage));
        CollectionScrollOffset = new Vector(0, 0);
        if (databaseService == null)
            return;

        Collection = await databaseService.GetCollectionAsync(id) ??
                     new CollectionDTO(fallbackName ?? "Collection") { Id = id };
        await RefreshItems();
    }

    public void GoToPreviousPage() => CurrentPage = CurrentPage - 1;
    public void GoToNextPage() => CurrentPage = CurrentPage + 1;
    public void GoToFirstPage() => CurrentPage = 1;
    public void GoToLastPage() => CurrentPage = TotalPages;
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
    public void SelectAll() => SetSelection(true);
    public void DeselectAll() => SetSelection(false);
    public void RemoveSelected() => Placeholder("Remove Selected");
    public void DeleteSelected() => Placeholder("DELETE selected");
    public void DeleteCollection() => Placeholder("Delete collection");
    public void DeleteCollectionAndImages() => Placeholder("Delete collection and images");
    public void Export() => Placeholder("Export");
    public void ExportSelected() => Placeholder("Export selected");
    public void Placeholder(string action) => windowService?.ShowNoticeWindow($"{action} is not implemented yet.");

    public void ShowCollectionInfo()
    {
        if (Collection == null)
            return;

        var folders = string.Join(", ", Collection.FolderIds);
        windowService?.ShowNoticeWindow(
            $"Id: {Collection.Id}\nName: {Collection.Name}\nCreated: {Collection.CreatedAt.ToLocalTime():g}\n" +
            $"Updated: {Collection.UpdatedAt.ToLocalTime():g}\nLast viewed: {Collection.LastViewed?.ToLocalTime().ToString("g") ?? "Never"}\n" +
            $"Folder IDs: {folders}", "Collection information");
    }

    public void ShowTagEditor()
    {
        // TODO: Implement tag editor
    }

    public async Task RefreshItems()
    {
        if (databaseService == null || collectionId == null)
            return;

        try
        {
            var (content, totalItems) = await databaseService.GetCollectionContents(collectionId.Value, CurrentPage - 1,
                PageSize, SortColumn, SortDirection, SearchText);
            var collectionTotal = totalItems;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var (_, unfilteredTotal) = await databaseService.GetCollectionContents(collectionId.Value, 0, 1,
                    SortColumn, SortDirection);
                collectionTotal = unfilteredTotal;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CollectionItemCount = collectionTotal;
                OnPropertyChanged(nameof(CollectionItemCount));
                OnPropertyChanged(nameof(CollectionStatistics));
                TotalPages = (int)Math.Ceiling((double)totalItems / PageSize);
                foreach (var item in CollectionItems)
                    item.Dispose();
                CollectionItems.Clear();
                foreach (var item in content)
                {
                    var viewer = new MediaViewerViewModel(logger!, windowService!)
                    {
                        Name = item.OriginalFileName,
                        MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(item), serviceProvider!),
                        MediaOpenResources = new ShowMediaInSeparateWindow(windowService!),
                        ShowingThumbnail = true,
                        AllowSelection = true,
                    };
                    viewer.OnSelectionChanged += OnItemSelectionChanged;
                    CollectionItems.Add(viewer);
                }

                if (pendingScrollOffsetRestore is { } scrollOffset)
                {
                    pendingScrollOffsetRestore = null;
                    var restoreVersion = scrollOffsetRestoreVersion;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        Dispatcher.UIThread.Post(() =>
                            {
                                if (restoreVersion == scrollOffsetRestoreVersion)
                                    CollectionScrollOffset = scrollOffset;
                            },
                            DispatcherPriority.Background);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to load collection", ex);
        }
    }

    public void Dispose()
    {
        foreach (var item in CollectionItems)
            item.Dispose();
        Hamburger.Dispose();
    }

    private void SetSelection(bool selected)
    {
        foreach (var item in CollectionItems)
            item.Selected = selected;
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void OnItemSelectionChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(SelectedCount));

    private async Task SetPairedImageMode(bool enabled)
    {
        if (Collection == null || databaseService == null)
            return;

        try
        {
            await databaseService.SetCollectionImageGroupSizeAsync(Collection.Id, enabled ? 2 : 1);
            Collection.ImageGroupSize = enabled ? 2 : 1;
            OnPropertyChanged(nameof(IsPairedImageMode));
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to change paired image mode", ex);
            OnPropertyChanged(nameof(IsPairedImageMode));
        }
    }

    private void ResetScrollPositionCache(bool resetPage)
    {
        pageScrollOffsets.Clear();
        pendingScrollOffsetRestore = null;
        ++scrollOffsetRestoreVersion;
        if (resetPage)
        {
            currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));
        }

        CollectionScrollOffset = new Vector(0, 0);
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);
        Hamburger.MenuItems.Add(new HamburgerMenuItem { Title = "Close", Command = new RelayCommand(Close) });
        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}
