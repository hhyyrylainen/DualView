using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualView.GUI.ViewModels;

public sealed class ImportSectionViewModel : ViewModelBase, IDisposable
{
    private readonly IClientDatabaseService? databaseService;
    private readonly ILogger? logger;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;
    private readonly ISignalRService? signalRService;
    private readonly long id;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly ICollectionBrowse? collectionBrowse;

    private CancellationTokenSource? nameSaveCancellation;
    private bool isInitialized;
    private bool isRefreshingActive;

    private int targetNameSearchVersion;

    // Preview constructor
    public ImportSectionViewModel()
    {
        databaseService = null!;
        signalRService = null;
        Name = "Test name";

        FolderPicker = new FolderPickerViewModel();
        RemoveAfterImport = true;
        id = -1;
        IsActive = true;
        TargetFolderId = 1;
        isInitialized = true;
    }

    [ActivatorUtilitiesConstructor]
    public ImportSectionViewModel(UploadSectionDTO section, IClientDatabaseService databaseService,
        ILogger? logger, IWindowService? windowService, ILogger<FolderPickerViewModel>? folderPickerLogger,
        IServiceProvider? serviceProvider, ISignalRService? signalRService)
    {
        this.databaseService = databaseService;
        this.logger = logger;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;
        this.signalRService = signalRService;
        collectionBrowse = new ImportSectionBrowse(section.Id, databaseService,
            serviceProvider ?? Program.ServiceProvider!);

        id = section.Id;
        Name = section.Name;
        KeepEvenWhenEmpty = section.KeepTarget;
        RemoveAfterImport = section.RemoveAfterImport;
        IsActive = section.Selected;
        TargetFolderId = section.TargetFolderId;

        // If one service is given, assume all are available
        FolderPicker = folderPickerLogger != null && windowService != null
            ? new FolderPickerViewModel(folderPickerLogger, databaseService, windowService, serviceProvider!)
            : new FolderPickerViewModel();
        FolderPicker.PropertyChanged += OnFolderPickerPropertyChanged;
        _ = InitializeFolderPathAsync(section.TargetFolderId);

        foreach (var media in section.Media)
        {
            var viewer = new MediaViewerViewModel(logger ?? NullLogger.Instance, windowService!)
            {
                Name = media.OriginalFileName,
                MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(media),
                    serviceProvider ?? Program.ServiceProvider!),
                MediaOpenResources = new ShowMediaInSeparateWindow(windowService!, collectionBrowse),
                ShowingThumbnail = true,
                AllowSelection = true,
                Selected = false,
            };
            Media.Add(viewer);
            viewer.OnSelectionChanged += OnMediaSelectionChanged;
        }

        isInitialized = true;
        if (signalRService != null)
        {
            signalRService.OnUploadSectionActiveChanged += OnUploadSectionActiveChanged;
            signalRService.OnUploadSectionUpdated += OnUploadSectionUpdated;
            signalRService.OnUploadSectionContentsUpdated += OnUploadSectionContentsUpdated;
        }
    }

    public ObservableCollection<MediaViewerViewModel> Media { get; } = new();
    public FolderPickerViewModel FolderPicker { get; }
    public long Id => id;

    public bool CollectionTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ImagesTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsCollapsed
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && isInitialized)
                ScheduleNameSave();
        }
    }

    public string? SelectedTargetName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (value != null && Name != value)
                    Name = value;
            }
        }
    }

    public List<string> TargetNameSuggestions
    {
        get;
        set => SetProperty(ref field, value);
    } = new();

    public long TargetFolderId
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && isInitialized)
                SaveImmediately();
        }
    }

    public bool KeepEvenWhenEmpty
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && isInitialized)
                SaveImmediately();
        }
    }

    public bool RemoveAfterImport
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && isInitialized)
                SaveImmediately();
        }
    }

    public bool IsActive
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && isInitialized && !isRefreshingActive)
                SaveActiveImmediately();
        }
    }

    public int ImageCount => Media.Count;
    public int SelectedCount => Media.Count(item => item.Selected);

    public async Task LoadTargetNameSuggestionsAsync(string search)
    {
        var searchVersion = ++targetNameSearchVersion;
        if (search.Trim().Length <= 2 || databaseService == null)
        {
            TargetNameSuggestions = [];
            return;
        }

        try
        {
            var names = await databaseService.SearchUploadTargetNamesAsync(search);
            if (searchVersion != targetNameSearchVersion)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (searchVersion == targetNameSearchVersion)
                    TargetNameSuggestions = names;
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load upload target name suggestions");
        }
    }

    public void SelectCollectionTab()
    {
        CollectionTabSelected = true;
        ImagesTabSelected = false;
        IsCollapsed = false;
    }

    public void SelectImagesTab()
    {
        CollectionTabSelected = false;
        ImagesTabSelected = true;
        IsCollapsed = false;
    }

    public void Collapse()
    {
        CollectionTabSelected = false;
        ImagesTabSelected = false;
        IsCollapsed = true;
    }

    public async Task SetActiveAsync()
    {
        if (databaseService == null)
            return;
        await databaseService.SetUploadSectionActiveAsync(IsActive ? id : null);
    }

    public async Task SaveAsync()
    {
        CancelNameSave();
        await SaveCurrentValuesAsync();
    }

    public async Task ImportAsync()
    {
        if (databaseService == null)
            return;

        try
        {
            await SaveAsync();

            var selected = Media.Where(item => item.Selected)
                .Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList();
            await databaseService.ImportUploadSectionAsync(id, selected.Count == 0 ? null : selected);
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to import section", ex);
        }
    }

    public async Task DeleteAsync()
    {
        if (databaseService == null)
            return;

        if (Media.Count > 0)
        {
            var proceed = windowService == null || await windowService.ShowConfirmationWindow(
                "DELETE import section?",
                $"This import section contains {Media.Count} image(s). Delete it and abandon these images?",
                true) == true;

            if (!proceed)
                return;
        }

        try
        {
            await databaseService.DeleteUploadSectionAsync(id);
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to delete import section", ex);
        }
    }

    public async Task RemoveSelectedAsync()
    {
        var selected = Media.Where(item => item.Selected).ToList();
        await databaseService!.RemoveMediaFromUploadSectionAsync(id,
            selected.Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList());
        foreach (var item in selected)
        {
            item.OnSelectionChanged -= OnMediaSelectionChanged;
            item.Dispose();
            Media.Remove(item);
        }

        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void DeselectAll()
    {
        foreach (var item in Media)
            item.Selected = false;
    }

    public async Task ReverseImagesAsync()
    {
        if (databaseService == null || Media.Count < 2)
            return;

        var selectedItems = Media.Where(item => item.Selected).ToList();
        if (selectedItems.Count == 1)
            return;

        var desiredOrder = Media.ToList();
        if (selectedItems.Count == 0)
        {
            desiredOrder.Reverse();
        }
        else
        {
            var selectedIndices = Media
                .Select((item, index) => item.Selected ? index : -1)
                .Where(index => index >= 0)
                .ToList();
            for (var index = 0; index < selectedIndices.Count; ++index)
                desiredOrder[selectedIndices[index]] = selectedItems[selectedItems.Count - 1 - index];
        }

        try
        {
            await databaseService.ReorderUploadSectionAsync(id,
                desiredOrder.Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList());

            for (var index = 0; index < desiredOrder.Count; ++index)
            {
                var currentIndex = Media.IndexOf(desiredOrder[index]);
                if (currentIndex != index)
                    Media.Move(currentIndex, index);
            }
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to reverse images", ex);
        }
    }

    public async Task RefreshDetailsAsync()
    {
        if (databaseService == null)
            return;

        var section = await databaseService.GetUploadSectionAsync(id);
        if (section == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => ApplySectionDetails(section));
    }

    public async Task RefreshContentsAsync()
    {
        if (databaseService == null)
            return;

        var section = await databaseService.GetUploadSectionAsync(id);
        if (section == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => ApplySectionContents(section));
    }

    public void UpdateFromServer(UploadSectionDTO section)
    {
        ApplySectionDetails(section);
        ApplySectionContents(section);
    }

    public void Dispose()
    {
        isInitialized = false;
        foreach (var media in Media)
        {
            media.OnSelectionChanged -= OnMediaSelectionChanged;
            media.Dispose();
        }

        FolderPicker.PropertyChanged -= OnFolderPickerPropertyChanged;
        FolderPicker.Dispose();
        CancelNameSave();
        signalRService?.OnUploadSectionActiveChanged -= OnUploadSectionActiveChanged;
        signalRService?.OnUploadSectionUpdated -= OnUploadSectionUpdated;
        signalRService?.OnUploadSectionContentsUpdated -= OnUploadSectionContentsUpdated;
    }

    private async Task InitializeFolderPathAsync(long folderId)
    {
        if (folderId == MediaFolderInfo.RootFolderId)
        {
            FolderPicker.SelectedPath = "/";
            return;
        }

        try
        {
            FolderPicker.SelectedPath = await databaseService.GetMediaFolderPath(folderId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to resolve import target folder {FolderId}", folderId);
        }
    }

    private async void OnFolderPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderPickerViewModel.SelectedPath))
            return;

        var folder = await databaseService.GetMediaFolderFromPathAsync(FolderPicker.SelectedPath);
        if (folder != null)
        {
            TargetFolderId = folder.Id;
        }
    }

    private void OnUploadSectionActiveChanged(long? activeSectionId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            isRefreshingActive = true;
            try
            {
                IsActive = activeSectionId == id;
            }
            finally
            {
                isRefreshingActive = false;
            }
        });
    }

    private void OnUploadSectionUpdated(long sectionId)
    {
        if (sectionId == id)
            _ = RefreshDetailsAsync();
    }

    private void OnUploadSectionContentsUpdated(long sectionId)
    {
        if (sectionId == id)
            _ = RefreshContentsAsync();
    }

    private void ApplySectionDetails(UploadSectionDTO section)
    {
        isInitialized = false;
        try
        {
            Name = section.Name;
            KeepEvenWhenEmpty = section.KeepTarget;
            RemoveAfterImport = section.RemoveAfterImport;
            TargetFolderId = section.TargetFolderId;
            IsActive = section.Selected;
        }
        finally
        {
            isInitialized = true;
        }

        _ = InitializeFolderPathAsync(section.TargetFolderId);
    }

    private void ApplySectionContents(UploadSectionDTO section)
    {
        var existingViewers = Media.ToDictionary(item => ((ServerMediaSource)item.MediaToShow!).ServerId);
        var desiredViewers = new List<MediaViewerViewModel>();
        foreach (var media in section.Media)
        {
            if (existingViewers.Remove(media.Id, out var viewer))
            {
                viewer.Name = media.OriginalFileName;
            }
            else
            {
                viewer = CreateMediaViewer(media);
            }

            desiredViewers.Add(viewer);
        }

        foreach (var removedViewer in existingViewers.Values)
        {
            removedViewer.OnSelectionChanged -= OnMediaSelectionChanged;
            removedViewer.Dispose();
        }

        for (var index = 0; index < desiredViewers.Count; ++index)
        {
            if (index < Media.Count && ReferenceEquals(Media[index], desiredViewers[index]))
                continue;

            var currentIndex = Media.IndexOf(desiredViewers[index]);
            if (currentIndex >= 0)
            {
                Media.Move(currentIndex, index);
            }
            else
            {
                Media.Insert(index, desiredViewers[index]);
            }
        }

        while (Media.Count > desiredViewers.Count)
            Media.RemoveAt(Media.Count - 1);

        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private MediaViewerViewModel CreateMediaViewer(MediaFileDTO media)
    {
        var viewer = new MediaViewerViewModel(logger ?? NullLogger.Instance, windowService!)
        {
            Name = media.OriginalFileName,
            MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(media),
                serviceProvider ?? Program.ServiceProvider!),
            MediaOpenResources = new ShowMediaInSeparateWindow(windowService!, collectionBrowse),
            ShowingThumbnail = true,
            AllowSelection = true,
            Selected = false,
        };
        viewer.OnSelectionChanged += OnMediaSelectionChanged;
        return viewer;
    }

    private void SaveActiveImmediately()
    {
        _ = SaveActiveImmediatelyAsync();
    }

    private async Task SaveActiveImmediatelyAsync()
    {
        try
        {
            if (id >= 0)
                await SetActiveAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to update active import section");
        }
    }

    private void ScheduleNameSave()
    {
        CancelNameSave();
        nameSaveCancellation = new CancellationTokenSource();
        _ = SaveNameAfterDelayAsync(nameSaveCancellation.Token);
    }

    private async Task SaveNameAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            await SaveCurrentValuesAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save import section name");
        }
    }

    private void SaveImmediately()
    {
        CancelNameSave();
        _ = SaveImmediatelyAsync();
    }

    private async Task SaveImmediatelyAsync()
    {
        try
        {
            await SaveCurrentValuesAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save import section settings");
        }
    }

    private async Task SaveCurrentValuesAsync()
    {
        if (id < 0)
            return;

        await saveLock.WaitAsync();
        try
        {
            await databaseService.SaveUploadSectionAsync(new UploadSectionDTO
            {
                Id = id, Name = Name.Trim(), KeepTarget = KeepEvenWhenEmpty, RemoveAfterImport = RemoveAfterImport,
                TargetFolderId = TargetFolderId,
            });
        }
        finally
        {
            saveLock.Release();
        }
    }

    private void CancelNameSave()
    {
        nameSaveCancellation?.Cancel();
        nameSaveCancellation?.Dispose();
        nameSaveCancellation = null;
    }

    private void OnMediaSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedCount));
    }
}
