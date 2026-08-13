using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Services;
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

    private long currentConfiguredMediaId;
    private long currentMediaFileId;

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

    public string ImageInfo
    {
        get;
        set => SetProperty(ref field, value);
    } = "Loading...";

    public void ShowMedia(IVisualMediaSource source, IMediaAssociatedWindows? extraData)
    {
        Media.MediaToShow = source;
        Media.MediaOpenResources = extraData;

        // Initialize extra data
        if (source is ServerMediaSource serverMediaSource)
        {
            _ = LoadMediaFileStatus(serverMediaSource.ServerId);
        }
        else
        {
            HasServerMedia = false;
            IsTemporary = false;
            currentConfiguredMediaId = 0;
            currentMediaFileId = 0;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var name = await source.GetName();

                Title = $"DualView - {name}";
            }
            catch (Exception e)
            {
                windowService?.ShowErrorWindow("Failed to get media name", e);
            }
        });
    }

    public void OpenMediaWindow()
    {
        windowService?.ShowSingletonWindow<MediaCollectionWindowViewModel>();
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
                var data = await clientDatabaseService.GetConfiguredMediaAsync(id);

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
                if (frame == null)
                {
                    ImageInfo = "No image loaded";
                }
                else
                {
                    ImageInfo = $"{frame.PixelSize.Width}x{frame.PixelSize.Height}";
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
            var media = await clientDatabaseService.GetConfiguredMediaAsync(configuredMediaId);
            if (media?.MediaFile == null)
            {
                HasServerMedia = false;
                return;
            }

            currentConfiguredMediaId = media.Id;
            currentMediaFileId = media.MediaFile.Id;
            HasServerMedia = true;
            IsTemporary = media.MediaFile.IsTemporary;
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to get media keep status", e);
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
            { Title = "Open Media Collection", Command = new RelayCommand(OpenMediaWindow) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Edit This...", Command = new RelayCommand(OpenMediaEditorSetup) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Copy ID", Command = new RelayCommand(RequestIDCopy) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Export...", Command = new RelayCommand(SaveMedia) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Edit Contained Folders...", Command = new RelayCommand(EditContainedFolders) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
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
