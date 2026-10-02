using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

public sealed class ScannedCollectionViewModel : ViewModelBase, IDisposable, ICollectionBrowse, IMediaSelectionBrowse
{
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;
    private readonly ILogger logger;
    private readonly IWindowService? windowService;
    private CancellationTokenSource? statusRefreshCancellation;
    private int targetNameSearchVersion;
    private long id;

    public ScannedCollectionViewModel()
    {
        Name = "Gallery";
        TargetName = string.Empty;
        logger = NullLogger.Instance;
    }

    [ActivatorUtilitiesConstructor]
    public ScannedCollectionViewModel(ScannedCollectionDTO collection, IClientDatabaseService databaseService,
        IBackendAPI backendAPI, IServiceProvider serviceProvider, ILogger logger, IWindowService windowService)
    {
        this.databaseService = databaseService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.windowService = windowService;

        UpdateFromServer(collection);
        _ = PollStatusLoopAsync();
    }

    public ObservableCollection<MediaViewerViewModel> Media { get; } = new();
    public long Id => id;

    public string GalleryUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string Name
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string TargetName
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string? SelectedTargetName
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value != null && TargetName != value)
                TargetName = value;
        }
    }

    public List<string> TargetNameSuggestions
    {
        get;
        set => SetProperty(ref field, value);
    } = new();

    public string? ScanError
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string StatusText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string AcceptText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Accept (0)";

    public bool ScanFailed
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool ScanPaused
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool CanPause
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool CanResume
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string? CurrentlyScannedUrl
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public event Action<long, bool>? OnSelectionChanged;

    public void UpdateFromServer(ScannedCollectionDTO collection)
    {
        id = collection.Id;
        GalleryUrl = collection.GalleryUrl;
        if (string.IsNullOrWhiteSpace(TargetName))
            TargetName = collection.Name;

        ApplyStatus(new ScannedCollectionStatusDTO
        {
            Id = collection.Id,
            Name = collection.Name,
            ScanFailed = collection.ScanFailed,
            ScanPaused = collection.ScanPaused,
            ScanError = collection.ScanError,
            CurrentlyScannedUrl = collection.CurrentlyScannedUrl,
            FoundMediaCount = collection.FoundMediaCount,
            SelectedMediaCount = collection.SelectedMediaCount,
        });

        var existing = Media.ToDictionary(item => ((ScannedMediaSource)item.MediaToShow!).ServerId);
        var refreshedMedia = new List<MediaViewerViewModel>(collection.Media.Count);
        foreach (var item in collection.Media)
        {
            if (existing.Remove(item.Id, out var viewer))
            {
                viewer.Name = item.Name;
                viewer.Selected = item.Selected;
            }
            else
            {
                viewer = new MediaViewerViewModel(logger, windowService!)
                {
                    Name = item.Name,
                    MediaToShow = new ScannedMediaSource(collection.Id, item, serviceProvider!),
                    MediaOpenResources = new ShowMediaInSeparateWindow(windowService!, this),
                    ShowingThumbnail = true,
                    AllowSelection = true,
                    Selected = item.Selected,
                    CustomWidth = 110,
                    CustomHeight = 145,
                };

                viewer.OnSelectionChanged += OnMediaSelectionChanged;
            }

            refreshedMedia.Add(viewer);
        }

        foreach (var removedViewer in existing.Values)
        {
            removedViewer.OnSelectionChanged -= OnMediaSelectionChanged;
            removedViewer.Dispose();
            Media.Remove(removedViewer);
        }

        for (var index = 0; index < refreshedMedia.Count; ++index)
        {
            var viewer = refreshedMedia[index];
            if (index < Media.Count && ReferenceEquals(Media[index], viewer))
                continue;

            var currentIndex = Media.IndexOf(viewer);
            if (currentIndex >= 0)
            {
                Media.Move(currentIndex, index);
            }
            else
            {
                Media.Insert(index, viewer);
            }
        }

        UpdateDerivedProperties();
    }

    public async Task PauseAsync()
    {
        if (backendAPI != null)
            await backendAPI.PauseScannedCollectionAsync(id);
    }

    public async Task ResumeAsync()
    {
        if (backendAPI != null)
            await backendAPI.ResumeScannedCollectionAsync(id);
    }

    public async Task DeleteAsync()
    {
        if (backendAPI == null)
            return;

        try
        {
            await backendAPI.DeleteScannedCollectionAsync(id);
            Dispose();
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to delete gallery scan", ex);
        }
    }

    public async Task AcceptAsync()
    {
        var name = TargetName.Trim();
        if (name.Length == 0)
        {
            windowService?.ShowErrorWindow("Cannot accept gallery", new ArgumentException("Enter a target name."));
            return;
        }

        try
        {
            await backendAPI!.AcceptScannedCollectionAsync(id, name,
                Media.Where(item => item.Selected)
                    .Select(item => ((ScannedMediaSource)item.MediaToShow!).ServerId).ToList());

            var collection = await databaseService!.GetScannedCollectionAsync(id);
            if (collection != null)
                UpdateFromServer(collection);
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to accept gallery", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            statusRefreshCancellation?.Cancel();
            statusRefreshCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Ignore already disposed tokens
        }

        foreach (var item in Media)
        {
            item.OnSelectionChanged -= OnMediaSelectionChanged;
            item.Dispose();
        }
    }

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
            logger.LogError(ex, "Failed to load scanned gallery target name suggestions");
        }
    }

    public Task<(int Count, int? Index)> GetBrowseInfoAsync(long? mediaId)
    {
        var index = mediaId.HasValue
            ? Media.ToList().FindIndex(item => item.MediaToShow is ScannedMediaSource source &&
                                                source.ServerId == mediaId.Value)
            : -1;
        return Task.FromResult<(int Count, int? Index)>((Media.Count, index >= 0 ? index : null));
    }

    public Task<IVisualMediaSource?> GetMediaAsync(int index)
    {
        if (index < 0 || index >= Media.Count || Media[index].MediaToShow is not IVisualMediaSource mediaSource)
            return Task.FromResult<IVisualMediaSource?>(null);

        return Task.FromResult<IVisualMediaSource?>(mediaSource.Clone());
    }

    public bool IsSelected(long mediaId)
    {
        return Media.FirstOrDefault(item => item.MediaToShow is ScannedMediaSource source &&
                                            source.ServerId == mediaId)?.Selected ?? false;
    }

    public void SetSelected(long mediaId, bool selected)
    {
        var viewer = Media.FirstOrDefault(item => item.MediaToShow is ScannedMediaSource source &&
                                                 source.ServerId == mediaId);
        if (viewer != null)
            viewer.Selected = selected;
    }

    public void NotifySelectionChanged(long mediaId, bool selected)
    {
        OnSelectionChanged?.Invoke(mediaId, selected);
    }

    private void OnMediaSelectionChanged(object? sender, EventArgs e)
    {
        AcceptText = $"Accept ({Media.Count(item => item.Selected)})";
        if ((sender as MediaViewerViewModel)?.MediaToShow is ScannedMediaSource source && databaseService != null &&
            sender is MediaViewerViewModel viewer)
        {
            _ = databaseService.SetScannedMediaSelectedAsync(source.ServerId, viewer.Selected);
            NotifySelectionChanged(source.ServerId, viewer.Selected);
        }
    }

    private async Task PollStatusLoopAsync()
    {
        if (databaseService == null)
            return;

        statusRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = statusRefreshCancellation.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await databaseService.GetScannedCollectionStatusAsync(id);
                if (status == null)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() => ApplyStatus(status));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to refresh scanned gallery status {GalleryId}", id);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void ApplyStatus(ScannedCollectionStatusDTO status)
    {
        Name = status.Name;
        ScanError = status.ScanError;
        ScanFailed = status.ScanFailed;
        ScanPaused = status.ScanPaused;
        CurrentlyScannedUrl = status.CurrentlyScannedUrl;
        UpdateDerivedProperties();
    }

    private void UpdateDerivedProperties()
    {
        StatusText = ScanError ?? (ScanPaused ? "Paused" :
            CurrentlyScannedUrl == null ? $"Scan complete ({Media.Count} images)" :
            $"Scanning {Media.Count} images…");
        AcceptText = $"Accept ({Media.Count(item => item.Selected)})";
        CanPause = !ScanFailed && !ScanPaused && CurrentlyScannedUrl != null;
        CanResume = ScanFailed || ScanPaused;
    }
}
