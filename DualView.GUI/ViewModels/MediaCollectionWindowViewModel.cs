using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
    private const long UncategorizedCollectionId = 1;

    private readonly ILogger<MediaCollectionWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackendAPI? backendAPI;
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
    private CancellationTokenSource? visualSimilarityCancellation;
    private List<long>? visualSimilarityOrder;
    private CollectionMediaRemovalResult? latestRemoval;
    private ICollectionBrowse? collectionBrowse;

    public MediaCollectionWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        CollectionItems.Add(new MediaViewerViewModel { Name = "Test item", AllowSelection = true });
        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public MediaCollectionWindowViewModel(ILogger<MediaCollectionWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService databaseService, IBackendStatusService backendStatusService,
        IServiceProvider serviceProvider, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.backendAPI = backendAPI;
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
                OnPropertyChanged(nameof(DeleteCollectionHeader));
                OnPropertyChanged(nameof(CanDeleteCollection));
                OnPropertyChanged(nameof(CollectionCreatedAt));
                OnPropertyChanged(nameof(CollectionStatistics));
                OnPropertyChanged(nameof(IsPairedImageMode));
            }
        }
    }

    public string WindowCollectionTitle => "DualView - " + (Collection?.Name ?? "No collection selected");
    public string DeleteCollectionHeader => Collection?.IsDeleted == true ? "Restore collection" : "Delete collection";
    public bool CanDeleteCollection => Collection?.Id != UncategorizedCollectionId;
    public DateTime? CollectionCreatedAt => Collection?.CreatedAt;
    public string CollectionStatistics => Collection == null ? "" : $"{CollectionItemCount} images";
    public int CollectionItemCount { get; private set; }

    public bool IsVisualSimilarityMode
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanNavigateBackwards));
                OnPropertyChanged(nameof(CanNavigateForwards));
            }
        }
    }

    public string VisualSimilarityStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public bool IsPairedImageMode
    {
        get => Collection?.ImageGroupSize == 2;
        set
        {
            if (Collection == null || value == IsPairedImageMode)
                return;

            _ = SavePairedImageMode(value);
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

    public bool CanNavigateBackwards => !IsVisualSimilarityMode && CurrentPage > 1;
    public bool CanNavigateForwards => !IsVisualSimilarityMode && CurrentPage < TotalPages;
    public int SelectedCount => CollectionItems.Count(item => item.Selected);
    public bool CanUndoRemoval => latestRemoval != null;

    public event EventHandler? CloseRequested;

    public async Task Initialize(long id, string? fallbackName = null)
    {
        ExitVisualSimilarityMode();
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
        collectionBrowse = new CollectionBrowse(id, databaseService, serviceProvider!);
        await RefreshItems();
    }

    public void GoToPreviousPage() => CurrentPage = CurrentPage - 1;
    public void GoToNextPage() => CurrentPage = CurrentPage + 1;
    public void GoToFirstPage() => CurrentPage = 1;
    public void GoToLastPage() => CurrentPage = TotalPages;
    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
    public void SelectAll() => SetSelection(true);
    public void DeselectAll() => SetSelection(false);
    public void RemoveSelected() => _ = RemoveSelectedAsync();
    public void UndoRemove() => _ = UndoRemoveAsync();
    public void DeleteSelected() => _ = DeleteSelectedAsync();
    public void SendSelectedToImport() => _ = SendSelectedToImportAsync();
    public void DeleteCollection() => _ = DeleteCollectionAsync();
    public void DeleteCollectionAndImages() => _ = DeleteCollectionAndImagesAsync();

    public void Export()
    {
        if (Collection == null || windowService == null)
            return;

        windowService.ShowWindow<ExportSetupWindowViewModel>(export =>
            export.Initialize(Collection, null));
    }

    public void ExportSelected()
    {
        if (Collection == null || windowService == null)
            return;

        var selectedIds = CollectionItems
            .Where(item => item.Selected && item.MediaToShow is ServerMediaSource)
            .Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId)
            .ToHashSet();

        if (selectedIds.Count == 0)
        {
            windowService.ShowNoticeWindow("No media is selected.");
            return;
        }

        windowService.ShowWindow<ExportSetupWindowViewModel>(export =>
            export.Initialize(Collection, selectedIds));
    }

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

    public void SetPairedImageMode(bool enabled)
    {
        _ = SavePairedImageMode(enabled);
    }

    public void StartVisualSimilaritySort()
    {
        _ = StartVisualSimilaritySortAsync();
    }

    public void FindMostSimilarToSelection()
    {
        _ = FindMostSimilarToSelectionAsync();
    }

    public void ExitVisualSimilarityMode()
    {
        visualSimilarityCancellation?.Cancel();
        visualSimilarityCancellation?.Dispose();
        visualSimilarityCancellation = null;

        if (!IsVisualSimilarityMode && string.IsNullOrEmpty(VisualSimilarityStatus))
            return;

        IsVisualSimilarityMode = false;
        visualSimilarityOrder = null;
        VisualSimilarityStatus = string.Empty;
        _ = RefreshItems();
    }

    public async Task RefreshItems()
    {
        if (databaseService == null || collectionId == null)
            return;

        try
        {
            List<MediaFileDTO> content;
            int totalItems;
            if (IsVisualSimilarityMode)
            {
                content = await databaseService.GetCollectionContents(collectionId.Value);
                if (visualSimilarityOrder != null)
                {
                    var contentById = content.ToDictionary(item => item.Id);
                    content = visualSimilarityOrder
                        .Where(contentById.ContainsKey)
                        .Select(mediaId => contentById[mediaId])
                        .ToList();
                }

                if (IsReversed)
                    content.Reverse();
                totalItems = content.Count;
            }
            else
            {
                (content, totalItems) = await databaseService.GetCollectionContents(collectionId.Value,
                    CurrentPage - 1, PageSize, SortColumn, SortDirection, SearchText);
            }

            var collectionTotal = totalItems;
            if (!IsVisualSimilarityMode && !string.IsNullOrWhiteSpace(SearchText))
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
                TotalPages = IsVisualSimilarityMode ? 1 : (int)Math.Ceiling((double)totalItems / PageSize);
                foreach (var item in CollectionItems)
                    item.Dispose();
                CollectionItems.Clear();
                foreach (var item in content)
                {
                    var viewer = new MediaViewerViewModel(logger!, windowService!)
                    {
                        Name = item.OriginalFileName,
                        MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(item), serviceProvider!),
                        MediaOpenResources = new ShowMediaInSeparateWindow(windowService!, collectionBrowse),
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
        try
        {
            visualSimilarityCancellation?.Cancel();
            visualSimilarityCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Not serious if already disposed
        }

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

    private List<long> GetSelectedMediaIds()
    {
        return CollectionItems
            .Where(item => item.Selected)
            .Select(item => item.MediaToShow)
            .OfType<ServerMediaSource>()
            .Select(source => source.Info.MediaFileId)
            .Distinct()
            .ToList();
    }

    private async Task RemoveSelectedAsync()
    {
        if (collectionId == null || databaseService == null)
            return;

        var mediaIds = GetSelectedMediaIds();
        if (mediaIds.Count == 0)
            return;

        try
        {
            var preview = await databaseService.PreviewCollectionMediaRemovalAsync(collectionId.Value, mediaIds);
            if (preview.OrphanedMediaIds.Count > 0)
            {
                var proceed = await windowService!.ShowConfirmationWindow("Remove selected media?",
                    $"{preview.OrphanedMediaIds.Count} selected image(s) would no longer be in any collection. " +
                    "They will be added to the Uncategorized collection. Continue?", true);
                if (proceed != true)
                    return;
            }

            latestRemoval = await databaseService.RemoveMediaFromCollectionAsync(collectionId.Value, mediaIds);
            OnPropertyChanged(nameof(CanUndoRemoval));
            await RefreshItems();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to remove selected media", ex);
        }
    }

    private async Task SendSelectedToImportAsync()
    {
        if (databaseService == null)
            return;

        var mediaIds = GetSelectedMediaIds();
        if (mediaIds.Count == 0)
            return;

        try
        {
            await databaseService.AddMediaToActiveUploadSectionAsync(mediaIds);
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to send selected media to import", ex);
        }
    }

    private async Task UndoRemoveAsync()
    {
        if (latestRemoval == null || databaseService == null)
            return;

        try
        {
            var removal = latestRemoval;
            latestRemoval = null;
            OnPropertyChanged(nameof(CanUndoRemoval));
            await databaseService.UndoCollectionMediaRemovalAsync(removal);
            if (removal.CollectionWasDeleted && Collection?.Id == removal.CollectionId)
            {
                Collection.IsDeleted = false;
                OnPropertyChanged(nameof(Collection));
                OnPropertyChanged(nameof(DeleteCollectionHeader));
            }

            await RefreshItems();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to undo media removal", ex);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (collectionId == null || databaseService == null)
            return;

        var mediaIds = GetSelectedMediaIds();
        if (mediaIds.Count == 0)
            return;

        try
        {
            var inOtherCollections = 0;
            foreach (var mediaId in mediaIds)
            {
                var collections = await databaseService.GetMediaCollectionsAsync(mediaId);
                if (collections.Any(id => id != collectionId.Value))
                    ++inOtherCollections;
            }

            if (inOtherCollections > 0)
            {
                var proceed = await windowService!.ShowConfirmationWindow("Delete selected media?",
                    $"{inOtherCollections} selected image(s) are also in another collection. " +
                    "They will be marked as deleted everywhere and removed during purge. Continue?", true);
                if (proceed != true)
                    return;
            }

            foreach (var mediaId in mediaIds)
                await databaseService.DeleteMediaAsync(mediaId);
            await RefreshItems();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to delete selected media", ex);
        }
    }

    private async Task DeleteCollectionAsync()
    {
        if (Collection == null || databaseService == null)
            return;

        if (Collection.IsDeleted)
        {
            await RestoreDeletedCollectionAsync();
            return;
        }

        try
        {
            var orphanedCount = await databaseService.GetCollectionOrphanedMediaCountAsync(Collection.Id);
            if (orphanedCount > 0)
            {
                var proceed = await windowService!.ShowConfirmationWindow("Delete collection?",
                    $"{orphanedCount} non-deleted image(s) would become orphaned and will eventually be moved to " +
                    "the Uncategorized collection. Continue?", true);
                if (proceed != true)
                    return;
            }

            await databaseService.DeleteCollectionAsync(Collection.Id);
            Collection.IsDeleted = true;
            OnPropertyChanged(nameof(Collection));
            OnPropertyChanged(nameof(DeleteCollectionHeader));
            await RefreshItems();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to delete collection", ex);
        }
    }

    private async Task DeleteCollectionAndImagesAsync()
    {
        if (Collection == null || databaseService == null)
            return;

        if (Collection.IsDeleted)
        {
            var undo = await windowService!.ShowConfirmationWindow("Undo collection deletion?",
                "This collection is already deleted. Do you want to undo the deletion?", true);
            if (undo == true)
                await RestoreDeletedCollectionAsync();
            return;
        }

        var start = await windowService!.ShowConfirmationWindow("DELETE collection AND images?",
            "This will mark the collection and every non-deleted image in it as deleted. " +
            "This is a dangerous operation. Continue?", true);
        if (start != true)
            return;

        try
        {
            var contents = await databaseService.GetCollectionContents(Collection.Id);
            var inOtherCollections = 0;
            foreach (var media in contents)
            {
                var collections = await databaseService.GetMediaCollectionsAsync(media.Id);
                if (collections.Any(id => id != Collection.Id))
                    ++inOtherCollections;
            }

            if (inOtherCollections > 0)
            {
                var proceed = await windowService.ShowConfirmationWindow("Confirm DELETE collection AND images?",
                    $"{inOtherCollections} image(s) are also in other collections and will be marked as deleted there too. " +
                    "Continue?", true);
                if (proceed != true)
                    return;
            }

            latestRemoval = await databaseService.DeleteCollectionAndImagesAsync(Collection.Id);
            Collection.IsDeleted = true;
            OnPropertyChanged(nameof(Collection));
            OnPropertyChanged(nameof(DeleteCollectionHeader));
            OnPropertyChanged(nameof(CanUndoRemoval));
            await RefreshItems();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to delete collection and images", ex);
        }
    }

    private async Task RestoreDeletedCollectionAsync()
    {
        if (Collection == null || databaseService == null)
            return;

        var undo = latestRemoval?.CollectionWasDeleted == true && latestRemoval.CollectionId == Collection.Id;
        var proceed = await windowService!.ShowConfirmationWindow(
            undo ? "Undo collection deletion?" : "Restore collection?",
            undo ? "Undo the collection and image deletion?" : "Restore this collection?", true);
        if (proceed != true)
            return;

        try
        {
            if (undo)
                await UndoRemoveAsync();
            else
            {
                await databaseService.RestoreCollectionAsync(Collection.Id);
                Collection.IsDeleted = false;
                OnPropertyChanged(nameof(Collection));
                OnPropertyChanged(nameof(DeleteCollectionHeader));
                await RefreshItems();
            }
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to restore collection", ex);
        }
    }

    private void OnItemSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedCount));
    }

    private async Task StartVisualSimilaritySortAsync()
    {
        if (Collection == null || backendAPI == null || IsVisualSimilarityMode)
            return;

        IsVisualSimilarityMode = true;
        visualSimilarityOrder = null;
        VisualSimilarityStatus = "Starting visual similarity sorting...";
        searchText = string.Empty;
        currentPage = 1;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(CurrentPage));

        visualSimilarityCancellation = new CancellationTokenSource();
        try
        {
            var operationId = await backendAPI.StartCollectionVisualSimilaritySort(Collection.Id);
            await MonitorVisualSimilaritySort(operationId, visualSimilarityCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to sort collection by visual similarity", ex);
            ExitVisualSimilarityMode();
        }
    }

    private async Task FindMostSimilarToSelectionAsync()
    {
        if (Collection == null || backendAPI == null || IsVisualSimilarityMode)
            return;

        var selectedImageIds = GetSelectedMediaIds();
        if (selectedImageIds.Count == 0)
        {
            windowService?.ShowNoticeWindow("Select at least one image first.");
            return;
        }

        IsVisualSimilarityMode = true;
        visualSimilarityOrder = null;
        VisualSimilarityStatus = "Starting visual similarity search...";
        searchText = string.Empty;
        currentPage = 1;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(CurrentPage));

        visualSimilarityCancellation = new CancellationTokenSource();
        try
        {
            var operationId = await backendAPI.StartCollectionVisualSimilaritySort(Collection.Id, selectedImageIds);
            await MonitorVisualSimilaritySort(operationId, visualSimilarityCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to find similar images", ex);
            ExitVisualSimilarityMode();
        }
    }

    private async Task MonitorVisualSimilaritySort(long operationId, CancellationToken cancellationToken)
    {
        if (backendAPI == null)
            throw new InvalidOperationException("Backend API is not initialized");

        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await backendAPI.GetOperationStatus(operationId);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var progress = Math.Clamp(status.CompletionFraction * 100, 0, 100);
                VisualSimilarityStatus =
                    $"Visual similarity mode: {progress:0.#}% — {status.Message ?? status.MainStatusText}";
            });

            if (status.Error && !status.Completed)
            {
                VisualSimilarityStatus = "Visual similarity sorting failed or was lost.";
                return;
            }

            if (status.Completed)
            {
                if (status.Error)
                {
                    VisualSimilarityStatus = "Visual similarity sorting failed.";
                }
                else
                {
                    visualSimilarityOrder = await backendAPI.GetCollectionVisualSimilarityOrder(operationId);
                    VisualSimilarityStatus =
                        "Visual similarity sorting complete. Most similar images are first (or last if reversed).";
                    await RefreshItems();
                }

                return;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task SavePairedImageMode(bool enabled)
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
