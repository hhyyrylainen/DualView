using System;
using System.Threading.Tasks;
using DualView.GUI.Controls;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class MediaEditorWindowViewModel : ViewModelBase, IDisposable, IRenderHook
{
    private readonly ILogger<MediaEditorWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;

    private long mediaId = -1;

    private DateTime lastEditsChange = DateTime.MinValue;

    private int latestEditId;

    private Point? mouseCropStart;
    private Point? mouseCropCurrent;
    private bool hookedRender;

    public MediaEditorWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
        MainMediaPreview = new MediaViewerViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public MediaEditorWindowViewModel(ILogger<MediaEditorWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, IBackendStatusService backendStatusService,
        IBackendAPI backendAPI, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        MainMediaPreview = new MediaViewerViewModel(logger, windowService)
        {
            ShowingThumbnail = false,
            AllowPanning = true,
            ShowName = true,
        };

        InitializeMenu();
    }

    public bool Loading
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool MouseCropMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value)
            {
                // Reset crop to show the full image and make the math work
                CropLeft = 0;
                CropRight = 0;
                CropTop = 0;
                CropBottom = 0;
            }

            if (!value)
            {
                // Turning off resets state
                mouseCropStart = null;
            }

            MainMediaPreview.ForceRedraw();
        }
    }

    public bool WantsToClose
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Name => ShownMedia?.Name ?? "No media selected";

    public string CalculatedStatsStr
    {
        get
        {
            if (ShownMedia == null)
                return "No media selected";

            ShownMedia.RefreshDerivedStatistics();

            if (ShownMedia.FrameCount > 1)
            {
                return
                    $"{ShownMedia.Width}x{ShownMedia.Height} {ShownMedia.FrameCount} frames @ {ShownMedia.FramesPerSecond} FPS";
            }

            return
                $"{ShownMedia.Width}x{ShownMedia.Height}";
        }
    }

    public ConfiguredMediaDTO? ShownMedia
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(CalculatedStatsStr));
            OnPropertyChanged(nameof(CropLeft));
            OnPropertyChanged(nameof(CropRight));
            OnPropertyChanged(nameof(CropTop));
            OnPropertyChanged(nameof(CropBottom));
            HasUnsavedChanges = false;
        }
    }

    // Edit controls
    public int CropLeft
    {
        get => ShownMedia?.CropLeft ?? 0;
        set
        {
            if (ShownMedia == null)
                return;

            if (ShownMedia.CropLeft == value)
                return;

            ShownMedia.CropLeft = value;
            OnPropertyChanged();
            TriggerEditsChanged();
            HasUnsavedChanges = true;
        }
    }

    public int MaxCropLeft
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int CropRight
    {
        get => ShownMedia?.CropRight ?? 0;
        set
        {
            if (ShownMedia == null)
                return;

            if (ShownMedia.CropRight == value)
                return;

            ShownMedia.CropRight = value;
            OnPropertyChanged();
            TriggerEditsChanged();
            HasUnsavedChanges = true;
        }
    }

    public int MaxCropRight
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int CropTop
    {
        get => ShownMedia?.CropTop ?? 0;
        set
        {
            if (ShownMedia == null)
                return;

            if (ShownMedia.CropTop == value)
                return;

            ShownMedia.CropTop = value;
            OnPropertyChanged();
            TriggerEditsChanged();
            HasUnsavedChanges = true;
        }
    }

    public int MaxCropTop
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int CropBottom
    {
        get => ShownMedia?.CropBottom ?? 0;
        set
        {
            if (ShownMedia == null)
                return;

            if (ShownMedia.CropBottom == value)
                return;

            ShownMedia.CropBottom = value;
            OnPropertyChanged();
            TriggerEditsChanged();
            HasUnsavedChanges = true;
        }
    }

    public int MaxCropBottom
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool HasUnsavedChanges
    {
        get;
        set => SetProperty(ref field, value);
    }

    // Other properties
    public MediaViewerViewModel MainMediaPreview { get; }

    public HamburgerMenuViewModel Hamburger { get; }

    public void InitializeFor(long mediaConfigurationId)
    {
        if (mediaId == mediaConfigurationId)
            return;

        mediaId = mediaConfigurationId;
        _ = LoadData();
    }

    public async Task SaveChanges()
    {
        if (ShownMedia == null || clientDatabaseService == null)
            return;

        if (!HasUnsavedChanges)
            return;

        try
        {
            await clientDatabaseService.SaveConfiguredMediaAsync(ShownMedia);

            Dispatcher.UIThread.Post(() => HasUnsavedChanges = false);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to save media changes");
            windowService?.ShowErrorWindow("Failed to save media changes", e);
        }
    }

    public void OnMouseCropPress(Point point)
    {
        if (!MouseCropMode)
            return;

        if (!hookedRender)
        {
            // This can't be in the constructor as the window is not initialized yet, so we need to do that here
            hookedRender = true;
            MainMediaPreview.SetRenderHook(this);
        }

        // Start
        if (mouseCropStart == null)
        {
            mouseCropStart = point;
            mouseCropCurrent = point;
            MainMediaPreview.ForceRedraw();
            return;
        }

        // We got the end point (second click)

        var start = mouseCropStart.Value;
        var end = point;

        var x1 = (int)Math.Min(start.X, end.X);
        var y1 = (int)Math.Min(start.Y, end.Y);
        var x2 = (int)Math.Max(start.X, end.X);
        var y2 = (int)Math.Max(start.Y, end.Y);

        mouseCropStart = null;
        mouseCropCurrent = null;
        MouseCropMode = false;

        if (ShownMedia?.MediaFile != null)
        {
            var width = ShownMedia.MediaFile.Width;
            var height = ShownMedia.MediaFile.Height;

            CropLeft = Math.Clamp(x1, 0, width);
            CropTop = Math.Clamp(y1, 0, height);

            // A bit unusually in this app, the right crop is calculated from the full width, so if we remove some
            // pixels from the left, then the right crop doesn't affect anything until that value, so we need to
            // calculate a real crop value like this to get the expected result
            CropRight = Math.Clamp(width - x2 + CropLeft, 0, width);
            CropBottom = Math.Clamp(height - y2 + CropTop, 0, height);
        }

        MainMediaPreview.ForceRedraw();
    }

    public void UpdateMouseCrop(Point point)
    {
        if (!MouseCropMode)
            return;

        if (!hookedRender)
        {
            hookedRender = true;
            MainMediaPreview.SetRenderHook(this);
        }

        mouseCropCurrent = point;
        MainMediaPreview.ForceRedraw();
    }

    /// <summary>
    ///   Special rendering on the main media viewer
    /// </summary>
    public void Render(DrawingContext context, Rect destRect, Size sourceSize)
    {
        if (!MouseCropMode || ShownMedia?.MediaFile == null)
            return;

        var mouse = mouseCropCurrent;
        if (mouse == null)
            return;

        var imageWidth = ShownMedia.MediaFile.Width;
        var imageHeight = ShownMedia.MediaFile.Height;

        // Scale factors
        var scaleX = destRect.Width / imageWidth;
        var scaleY = destRect.Height / imageHeight;

        var crosshairPen = new Pen(Brushes.White, 1);
        var crosshairPenSecondary = new Pen(Brushes.Black, 1) { DashStyle = DashStyle.Dash };

        // Draw crosshair
        var centerX = destRect.X + mouse.Value.X * scaleX;
        var centerY = destRect.Y + mouse.Value.Y * scaleY;

        context.DrawLine(crosshairPen, new Point(destRect.X, centerY), new Point(destRect.Right, centerY));
        context.DrawLine(crosshairPenSecondary, new Point(destRect.X, centerY), new Point(destRect.Right, centerY));

        context.DrawLine(crosshairPen, new Point(centerX, destRect.Y), new Point(centerX, destRect.Bottom));
        context.DrawLine(crosshairPenSecondary, new Point(centerX, destRect.Y), new Point(centerX, destRect.Bottom));

        // Draw a selection box
        if (mouseCropStart != null)
        {
            var start = mouseCropStart.Value;
            var end = mouse.Value;

            var x1 = Math.Min(start.X, end.X);
            var y1 = Math.Min(start.Y, end.Y);
            var x2 = Math.Max(start.X, end.X);
            var y2 = Math.Max(start.Y, end.Y);

            var rect = new Rect(
                destRect.X + x1 * scaleX,
                destRect.Y + y1 * scaleY,
                (x2 - x1) * scaleX,
                (y2 - y1) * scaleY);

            context.DrawRectangle(null, new Pen(Brushes.White, 2), rect);
            context.DrawRectangle(null, new Pen(Brushes.Black, 1) { DashStyle = DashStyle.Dash }, rect);
        }
    }

    public async Task SaveAllChanges()
    {
        if (HasUnsavedChanges)
            await SaveChanges();
    }

    public void Dispose()
    {
        Hamburger.Dispose();
        MainMediaPreview.Dispose();
    }

    private async Task LoadData()
    {
        if (clientDatabaseService == null || serviceProvider == null)
            return;

        Loading = true;

        try
        {
            var mediaInfo = await clientDatabaseService.GetConfiguredMediaAsync(mediaId);

            if (mediaInfo == null)
                throw new Exception("Media not found");

            if (mediaInfo.IsDeleted)
                windowService?.ShowNoticeWindow("Cannot edit a deleted media");

            if (mediaInfo.MediaFile == null || mediaInfo.MediaFile.IsDeleted)
                throw new Exception("Media file is deleted (or not loaded from server)");

            Dispatcher.UIThread.Post(() =>
            {
                ShownMedia = mediaInfo;
                Loading = false;

                MainMediaPreview.Name = mediaInfo.Name;

                lastEditsChange = DateTime.MinValue;
                MainMediaPreview.MediaToShow = null;
                TriggerEditsChanged();

                // Refresh controls
                // Need to use the original resolution as otherwise further edits will get messed up
                MaxCropLeft = mediaInfo.MediaFile.Width - 1;
                MaxCropRight = mediaInfo.MediaFile.Width - 1;
                MaxCropTop = mediaInfo.MediaFile.Height - 1;
                MaxCropBottom = mediaInfo.MediaFile.Height - 1;

                HasUnsavedChanges = false;
            });
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to fetch media data", e);
        }
    }

    /// <summary>
    ///   Called to show the new status
    /// </summary>
    private void TriggerEditsChanged()
    {
        _ = OnEditsChanged();
    }

    private async Task OnEditsChanged()
    {
        if (ShownMedia == null || serviceProvider == null)
            return;

        OnPropertyChanged(nameof(CalculatedStatsStr));

        var editId = ++latestEditId;

        // Rate limit updates
        var now = DateTime.Now;
        var elapsed = now - lastEditsChange;
        if (elapsed <= TimeSpan.FromMilliseconds(500))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500 - elapsed.TotalMilliseconds));
        }

        // Important to cancel unnecessary requests to not block up the server
        if (editId < latestEditId)
            return;

        lastEditsChange = DateTime.Now;
        var media = new ServerEditPreview(ShownMedia, serviceProvider);

        // Wait until loaded to get things working in the right order. Because otherwise we couldn't know really
        // when the image is fully loaded
        try
        {
            await media.RequestLoadFull();
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to load media preview with changes", e);
            return;
        }

        if (editId < latestEditId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            // Ignore edits that got here out of order
            if (editId < latestEditId)
                return;

            var oldMedia = MainMediaPreview.MediaToShow;
            MainMediaPreview.MediaToShow = media;

            if (oldMedia != null)
            {
                _ = Task.Run(async void () =>
                {
                    try
                    {
                        await Task.Delay(5000);
                        oldMedia.Dispose();
                    }
                    catch (Exception e)
                    {
                        logger?.LogError(e, "Failed to dispose old media");
                    }
                });
            }
        });
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Revert Changes", Command = new RelayCommand(() => _ = LoadData()) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}
