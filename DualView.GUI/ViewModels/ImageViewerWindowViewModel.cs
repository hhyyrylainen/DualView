using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Services;
using DualView.Shared.Utils;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ImageViewerWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly ISignalRService? signalRService;
    private readonly SemaphoreSlim browseLock = new(1, 1);

    private long currentConfiguredMediaId;
    private long currentMediaFileId;

    private ICollectionBrowse? collectionBrowse;
    private string currentMediaName = string.Empty;
    private int mediaDisplayVersion;
    private int? previousBrowseIndex;
    private HamburgerMenuItem? deleteMediaMenuItem;

    public delegate void ClipboardTextSetRequested(string text);

    public delegate Task<string?> MediaSaveRequested(string suggestedName);

    public event ClipboardTextSetRequested? OnClipboardTextSet;

    public event MediaSaveRequested? OnMediaSaveRequested;

    // Design time constructor
    public ImageViewerWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        Media = new MediaViewerViewModel
        {
            ShowingThumbnail = false,
            ShowName = false,
        };

        Media.OnDisplayedFrameChanged += CheckMediaDetails;
    }

    [ActivatorUtilitiesConstructor]
    public ImageViewerWindowViewModel(ILogger<ImageViewerWindowViewModel> logger, IWindowService windowService,
        IBackendStatusService backendStatusService, IClientDatabaseService clientDatabaseService,
        ISignalRService signalRService, IBackendAPI backendAPI)
    {
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
        this.backendAPI = backendAPI;
        this.signalRService = signalRService;
        Hamburger = new HamburgerMenuViewModel(backendStatusService);
        Media = new MediaViewerViewModel(logger, windowService)
        {
            ShowingThumbnail = false,
            ShowName = false,
            AllowPanning = true,
        };

        Media.OnDisplayedFrameChanged += CheckMediaDetails;

        signalRService.OnMediaUpdated += OnMediaUpdated;

        InitializeMenu();
    }

    public HamburgerMenuViewModel Hamburger { get; }

    public MediaViewerViewModel Media { get; }

    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    } = "DualView - Image Viewer";

    public bool HasServerMedia
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsTemporary
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsDeleted
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(DeleteMediaMenuHeader));

            if (deleteMediaMenuItem != null)
            {
                deleteMediaMenuItem.Title = value ? "Restore" : "Delete";
                ImageInfo = value ? "DELETED" : "Loading...";
                if (!value)
                {
                    CheckMediaDetails(this, EventArgs.Empty);
                }
            }
        }
    }

    public string DeleteMediaMenuHeader => IsDeleted ? "Restore this" : "Delete this";

    public bool HasBrowsing => collectionBrowse != null;

    public string ImageInfo
    {
        get;
        set => SetProperty(ref field, value);
    } = "Loading...";

    public string TagsString
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public string BrowsePosition
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(ImageInfo));
        }
    } = string.Empty;

    public void ShowMedia(IVisualMediaSource source, IMediaAssociatedWindows? extraData,
        ICollectionBrowse? browsingSupport)
    {
        Media.MediaToShow = source;

        if (!ReferenceEquals(collectionBrowse, browsingSupport))
            previousBrowseIndex = null;

        collectionBrowse = browsingSupport;
        Media.MediaOpenResources = extraData ??
                                   (windowService == null
                                       ? null
                                       : new ShowMediaInSeparateWindow(windowService, browsingSupport));
        var displayVersion = ++mediaDisplayVersion;
        BrowsePosition = string.Empty;
        OnPropertyChanged(nameof(ImageInfo));

        // Initialize extra data
        if (source is ServerMediaSource serverMediaSource)
        {
            HasServerMedia = false;
            IsDeleted = false;
            IsTemporary = false;
            TagsString = string.Empty;
            currentConfiguredMediaId = 0;
            currentMediaFileId = 0;
            _ = LoadMediaFileStatus(serverMediaSource.ServerId);
        }
        else
        {
            HasServerMedia = false;
            IsDeleted = false;
            IsTemporary = false;
            TagsString = string.Empty;
            currentConfiguredMediaId = 0;
            currentMediaFileId = 0;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var name = await source.GetName();

                currentMediaName = name;
                UpdateTitle(displayVersion);
                await UpdateBrowsePositionAsync(source, displayVersion);
            }
            catch (Exception e)
            {
                windowService?.ShowErrorWindow("Failed to get media name", e);
            }
        });
    }

    public void NavigateToAdjacentMedia(int offset)
    {
        _ = NavigateToAdjacentMediaAsync(offset);
    }

    public void SendToImport()
    {
        if (clientDatabaseService == null || !HasServerMedia || currentMediaFileId == 0)
        {
            windowService?.ShowNoticeWindow("Only server media can be sent to import.");
            return;
        }

        _ = SendToImportAsync();
    }

    public void GoToFirstPage()
    {
        if (collectionBrowse == null)
            return;

        _ = NavigateToBrowseIndexAsync(0);
    }

    public void GoToLastPage()
    {
        if (collectionBrowse == null)
            return;

        _ = GoToLastPageAsync();
    }

    public void ShowTagEditor()
    {
        throw new NotImplementedException();
    }

    public void ShowImageInfo()
    {
        if (clientDatabaseService == null || !HasServerMedia || currentMediaFileId == 0)
        {
            windowService?.ShowNoticeWindow("Detailed information is only available for server media.");
            return;
        }

        _ = ShowImageInfoAsync();
    }

    public void FinCollectionsThisIsIn()
    {
        // TODO: open a window that shows all collections this image is in and allows double clicking to open those collections.
        // windowService?.
    }

    public void OpenMediaEditorSetup()
    {
        var media = Media.MediaToShow;

        if (media is ServerMediaSource serverMediaSource)
        {
            windowService?.ShowMediaEditSetup(serverMediaSource.ServerId);
            return;
        }

        windowService?.ShowNoticeWindow("This media type cannot be edited.");
    }

    public void SaveMedia()
    {
        if (clientDatabaseService == null)
            return;

        var media = Media.MediaToShow;

        if (media is not ServerMediaSource serverMediaSource || OnMediaSaveRequested == null)
        {
            windowService?.ShowNoticeWindow("This media type cannot be saved.");
            return;
        }

        var id = serverMediaSource.ServerId;

        _ = Task.Run(async void () =>
        {
            try
            {
                var data = await clientDatabaseService.GetConfiguredMediaAsync(id, false);

                if (data?.MediaFile == null)
                    throw new Exception("Media not found");

                var suggestedName = data.MediaFile.OriginalFileName;

                var fullPath = await OnMediaSaveRequested(suggestedName);

                if (string.IsNullOrEmpty(fullPath))
                {
                    // Canceled
                    return;
                }

                // Create the folder
                var folder = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                // Then write the file
                await ServerMediaSource.DownloadFullMediaToLocalFile(data.Id, fullPath);
            }
            catch (Exception e)
            {
                windowService?.ShowErrorWindow("Failed to save media", e);
            }
        });
    }

    public void Dispose()
    {
        Hamburger.Dispose();
        Media.Dispose();
        browseLock.Dispose();
        Media.OnDisplayedFrameChanged -= CheckMediaDetails;

        if (signalRService != null)
        {
            signalRService.OnMediaUpdated -= OnMediaUpdated;
        }
    }

    private void CheckMediaDetails(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var frame = Media.CurrentFrameBitmap;

            // If this happens when immediately on close, then this can get an error
            try
            {
                if (IsDeleted)
                {
                    ImageInfo = "DELETED";
                }
                else if (frame == null)
                {
                    ImageInfo = "No image loaded";
                }
                else
                {
                    ImageInfo = $"{frame.PixelSize.Width}x{frame.PixelSize.Height}{
                        (string.IsNullOrEmpty(BrowsePosition) ? string.Empty : $" ({BrowsePosition})")}";
                }
            }
            catch (Exception)
            {
                ImageInfo = "Failed to get image size";
            }
        });
    }

    private async void RequestIDCopy()
    {
        try
        {
            string text = $"Unknown ID: {Title}";
            var source = Media.MediaToShow;

            if (source is ServerMediaSource serverMediaSource)
            {
                text = serverMediaSource.ServerId.ToString(CultureInfo.CurrentCulture);
            }
            else if (source != null)
            {
                text = await source.GetName();
            }

            Dispatcher.UIThread.Post(() => OnClipboardTextSet?.Invoke(text));
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to copy ID", e);
        }
    }

    private async Task LoadMediaFileStatus(long configuredMediaId)
    {
        if (clientDatabaseService == null)
            return;

        try
        {
            var media = await clientDatabaseService.GetConfiguredMediaAsync(configuredMediaId, false);
            if (media?.MediaFile == null)
            {
                HasServerMedia = false;
                return;
            }

            currentConfiguredMediaId = media.Id;
            currentMediaFileId = media.MediaFile.Id;
            HasServerMedia = true;
            IsDeleted = media.MediaFile.IsDeleted;
            IsTemporary = media.MediaFile.IsTemporary;

            var tags = await clientDatabaseService.GetMediaAppliedTagsAsync(currentMediaFileId);
            TagsString = string.Join(", ", tags.Select(AppliedTagText.ToText));
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to get media status", e);
        }
    }

    private async Task SendToImportAsync()
    {
        if (clientDatabaseService == null)
            return;

        try
        {
            await clientDatabaseService.AddMediaToActiveUploadSectionAsync([currentMediaFileId]);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to send media to import", e);
        }
    }

    private async Task GoToLastPageAsync()
    {
        try
        {
            var browseInfo = await collectionBrowse!.GetBrowseInfoAsync(null);
            if (browseInfo.Count > 0)
                await NavigateToBrowseIndexAsync(browseInfo.Count - 1);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to browse collection", e);
        }
    }

    private async Task NavigateToBrowseIndexAsync(int index)
    {
        if (collectionBrowse == null)
            return;

        try
        {
            var media = await collectionBrowse.GetMediaAsync(index);
            if (media == null)
                return;

            Media.MediaToShow?.Dispose();
            ShowMedia(media, Media.MediaOpenResources, collectionBrowse);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to browse collection", e);
        }
    }

    private async Task ShowImageInfoAsync()
    {
        if (clientDatabaseService == null)
            return;

        try
        {
            var media = await clientDatabaseService.GetMediaFileAsync(currentMediaFileId);
            if (media == null)
            {
                windowService?.ShowNoticeWindow("Media was not found.", "Media information");
                return;
            }

            windowService?.ShowNoticeWindow(
                $"Id: {media.Id}\nName: {media.OriginalFileName}\nHash: {media.Hash}\n" +
                $"Imported: {media.ImportedAt.ToLocalTime():g}\nUpdated: {media.UpdatedAt.ToLocalTime():g}\n" +
                $"Last viewed: {media.LastViewed?.ToLocalTime().ToString("g") ?? "Never"}\n" +
                $"{(media.ParentMediaId != null ? $"Parent: {media.ParentMediaId}" : "No parent")}\n" +
                $"Type: {media.MediaType.ToString()} Favourite: {media.IsFavorited} Stars: {media.Stars}\n" +
                $"Dimensions: {media.Width}x{media.Height}\nTemporary: {media.IsTemporary}\nDeleted: {media.IsDeleted}",
                "Media information");
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to get media information", e);
        }
    }

    private async Task NavigateToAdjacentMediaAsync(int offset)
    {
        if (collectionBrowse == null || Media.MediaToShow is not ServerMediaSource currentMedia)
            return;

        await browseLock.WaitAsync();
        try
        {
            var browseInfo = await collectionBrowse.GetBrowseInfoAsync(currentMedia.ServerId);
            var count = browseInfo.Count;
            if (count == 0)
                return;

            int targetIndex;
            if (browseInfo.Index is { } currentIndex)
            {
                previousBrowseIndex = currentIndex;
                targetIndex = currentIndex + offset;
            }
            else
            {
                // Keep the viewer at the position where the removed image was shown.
                targetIndex = previousBrowseIndex ?? 0;
            }

            // Wrapping around
            if (targetIndex < 0)
                targetIndex = count - 1;

            if (targetIndex >= count)
                targetIndex = 0;

            var nextMedia = await collectionBrowse.GetMediaAsync(targetIndex);
            if (nextMedia == null)
                return;

            Media.MediaToShow?.Dispose();
            ShowMedia(nextMedia, Media.MediaOpenResources, collectionBrowse);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to browse collection", e);
        }
        finally
        {
            browseLock.Release();
        }
    }

    private async Task UpdateBrowsePositionAsync(IVisualMediaSource source, int displayVersion)
    {
        if (collectionBrowse == null || source is not ServerMediaSource serverMediaSource)
            return;

        try
        {
            var browseInfo = await collectionBrowse.GetBrowseInfoAsync(serverMediaSource.ServerId);
            if (displayVersion != mediaDisplayVersion || browseInfo.Index == null)
                return;

            previousBrowseIndex = browseInfo.Index.Value;
            BrowsePosition = $"{browseInfo.Index.Value + 1} / {browseInfo.Count}";
            UpdateTitle(displayVersion);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to get collection position", e);
        }
    }

    private void UpdateTitle(int displayVersion)
    {
        if (displayVersion == mediaDisplayVersion)
        {
            Title = $"DualView - {currentMediaName}{
                (string.IsNullOrEmpty(BrowsePosition) ? string.Empty : $" ({BrowsePosition})")}";
        }
    }

    private void OnMediaUpdated(long mediaFileId)
    {
        if (mediaFileId == currentMediaFileId)
        {
            // Refresh state from the server
            _ = LoadMediaFileStatus(currentConfiguredMediaId);
        }
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Edit This...", Command = new RelayCommand(OpenMediaEditorSetup) });

        deleteMediaMenuItem = new HamburgerMenuItem
        {
            Title = "Delete",
            Command = new RelayCommand(ToggleDeleteMedia),
        };
        Hamburger.MenuItems.Add(deleteMediaMenuItem);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Copy ID", Command = new RelayCommand(RequestIDCopy) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Export...", Command = new RelayCommand(SaveMedia) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Edit Contained Folders...", Command = new RelayCommand(EditContainedFolders) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }

    private async void ToggleDeleteMedia()
    {
        if (clientDatabaseService == null || !HasServerMedia || currentMediaFileId == 0)
        {
            windowService?.ShowNoticeWindow("Only server media can be deleted.");
            return;
        }

        try
        {
            if (IsDeleted)
            {
                await clientDatabaseService.RestoreMediaAsync(currentMediaFileId);
            }
            else
            {
                await clientDatabaseService.DeleteMediaAsync(currentMediaFileId);
            }

            IsDeleted = !IsDeleted;
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow(IsDeleted ? "Failed to restore media" : "Failed to delete media", e);
        }
    }

    private void EditContainedFolders()
    {
        var source = Media.MediaToShow;
        if (source is not ServerMediaSource serverMediaSource)
        {
            windowService?.ShowNoticeWindow("Only server media can edit folders.");
            return;
        }

        windowService?.ShowEditMediaFolders(serverMediaSource.Info);
    }
}
