using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DualView.GUI.Controls;
using DualView.GUI.Models;
using DualView.GUI.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class MediaViewerViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger? logger;
    private readonly IWindowService? windowService;

    private readonly Lock taskLock = new();

    private IBrush normalBackground = Brushes.Transparent;
    private IBrush selectedBackground = Brushes.AliceBlue;

    private int lastWidth = -1;
    private int lastHeight = -1;

    private Task? imageLoadTask;

    /// <summary>
    ///   This is used to have only one active frame advance callback at a time
    /// </summary>
    private CancellationTokenSource? animationTokenSource;

    private long targetFrameTime;

    private bool disposed;

    public delegate Point RequestImageSpacePoint(double controlRelativeX, double controlRelativeY);

    public delegate IBrushPreviewTarget? RequestBrushPreviewTarget();

    public delegate void UpdateRenderHook(IRenderHook? hook);

    public event EventHandler? OnSelectionChanged;

    public event EventHandler? OnDisplayedFrameChanged;

    public event EventHandler? OnRequestPanReset;

    public event RequestImageSpacePoint? OnRequestImageSpacePoint;

    public event RequestBrushPreviewTarget? OnRequestBrushPreviewTarget;

    public event UpdateRenderHook? OnUpdateRenderHook;

    public bool HasAudio => MediaToShow?.HasAudio ?? false;

    public bool PlayAudio
    {
        get => MediaToShow?.PlayAudio ?? false;
        set
        {
            if (MediaToShow != null)
                MediaToShow.PlayAudio = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get;
        set => SetProperty(ref field, value);
    } = "Unknown item";

    // NOTE: these don't apply immediately. Any XAML file that includes this needs to do stuff like:
    // `<controls:MediaViewer Width="{Binding CustomWidth}" Height="{Binding CustomHeight}" Margin="2" />`
    public double CustomWidth
    {
        get;
        set => SetProperty(ref field, value);
    } = double.NaN;

    public double CustomHeight
    {
        get;
        set => SetProperty(ref field, value);
    } = double.NaN;

    public IBrush? BackgroundBrush
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool Selected
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);
            BackgroundBrush = value ? selectedBackground : normalBackground;
            OnSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsDragging
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsDragTargetBefore
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsDragTargetAfter
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public void SetDragTarget(bool insertAfter)
    {
        IsDragTargetBefore = !insertAfter;
        IsDragTargetAfter = insertAfter;
    }

    public void ClearDragTarget()
    {
        IsDragTargetBefore = false;
        IsDragTargetAfter = false;
    }

    /// <summary>
    ///   Whether the media viewer is currently visible (used to lazy load)
    /// </summary>
    public bool IsVisible
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);

            if (disposed)
                return;

            if (IsVisible && MediaToShow != null)
            {
                if (AutoThumbnailSize)
                    CalculateThumbnailSize();

                StartLoadingTask();
            }
            else if (!IsVisible)
            {
                // Unload the texture when not visible to save on rendering memory. When we become visible again the
                // texture will be reloaded.
                UnloadDisplay();
            }
        }
    }

    public bool AutoThumbnailSize
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ShowingThumbnail
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(CurrentFrameBitmap));

            if (IsVisible && MediaToShow != null)
                StartLoadingTask();
        }
    }

    /// <summary>
    ///   If true, the viewer allows panning and zooming the media
    /// </summary>
    public bool AllowPanning { get; set; }

    /// <summary>
    ///   Primary property to set to show the media
    /// </summary>
    public IVisualMediaSource? MediaToShow
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);
            targetFrameTime = 0;

            if (value == null)
            {
                UnloadDisplay();
                MediaOpenResources?.RefreshAvailableOptions(null);
            }
            else
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(MediaViewerViewModel));

                // Start loading the media
                if (AutoThumbnailSize)
                    CalculateThumbnailSize();

                // But only if already visible
                if (IsVisible)
                {
                    StartLoadingTask();
                }

                MediaOpenResources?.RefreshAvailableOptions(value);
            }
        }
    }

    public IMediaAssociatedWindows? MediaOpenResources
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);

            if (disposed)
                return;

            if (value == null)
            {
                WantsDoubleClick = false;
            }
            else
            {
                value.RefreshAvailableOptions(MediaToShow);

                WantsDoubleClick = value.DefaultDoubleClickAction != IMediaAssociatedWindows.DoubleClickAction.None;
            }

            OnPropertyChanged(nameof(HasViewAction));
            OnPropertyChanged(nameof(HasThumbnailAction));
            OnPropertyChanged(nameof(HasEditAction));
            OnPropertyChanged(nameof(HasFolderMoveAction));
            OnPropertyChanged(nameof(HasFolderAddAction));
            OnPropertyChanged(nameof(HasFolderManagerAction));
        }
    }

    /// <summary>
    ///   Access to previewing mouse brush
    /// </summary>
    public IBrushPreviewTarget? BrushPreviewTarget => OnRequestBrushPreviewTarget?.Invoke();

    public bool HasViewAction => MediaOpenResources?.HasViewAction ?? false;
    public bool HasThumbnailAction => MediaOpenResources?.HasThumbnailAction ?? false;
    public bool HasEditAction => MediaOpenResources?.HasEditAction ?? false;

    public bool HasFolderMoveAction => MediaOpenResources?.HasMoveToFolderAction ?? false;
    public bool HasFolderAddAction => MediaOpenResources?.HasAddToFolderAction ?? false;
    public bool HasFolderManagerAction => MediaOpenResources?.HasManageFoldersAction ?? false;

    public bool WantsDoubleClick
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool AllowSelection
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool AllowSelectionOnClick
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ShowName
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    ///   This is controlled automatically
    /// </summary>
    public WriteableBitmap? CurrentFrameBitmap
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    ///   Can be set by external code to display an overlay on top of the primary image
    /// </summary>
    public Bitmap? ExtraOverlayImage
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Bitmap? BackgroundSource
    {
        get;
        set => SetProperty(ref field, value);
    }

    // Design time constructor
    public MediaViewerViewModel()
    {
        GetBrushes();
        BackgroundBrush = normalBackground;

        Selected = true;
    }

    [ActivatorUtilitiesConstructor]
    public MediaViewerViewModel(ILogger<MediaViewerViewModel> logger, IWindowService windowService) :
        this((ILogger)logger, windowService)
    {
    }

    public MediaViewerViewModel(ILogger logger, IWindowService windowService)
    {
        this.logger = logger;
        this.windowService = windowService;

        GetBrushes();
        BackgroundBrush = normalBackground;
    }

    public bool OnDoubleClick()
    {
        if (!WantsDoubleClick)
            return false;

        if (MediaOpenResources == null)
            return false;

        switch (MediaOpenResources.DefaultDoubleClickAction)
        {
            case IMediaAssociatedWindows.DoubleClickAction.None:
                return false;
            case IMediaAssociatedWindows.DoubleClickAction.OpenView:
                OnViewResource();
                return true;
            case IMediaAssociatedWindows.DoubleClickAction.Activate:
                OnActivate();
                return true;
            case IMediaAssociatedWindows.DoubleClickAction.ViewThumbnail:
                OnViewThumbnail();
                return true;
            default:
                logger?.LogError("Unhandled double click action: {Action}",
                    MediaOpenResources.DefaultDoubleClickAction);
                throw new ArgumentOutOfRangeException();
        }
    }

    public void OnViewResource()
    {
        if (MediaOpenResources == null)
            return;

        try
        {
            MediaOpenResources.ShowView(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to open media", e);
        }
    }

    public void OnActivate()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.Activate(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to activate media", e);
        }
    }

    public void OnEditResource()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.StartEditAction(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to start media edit", e);
        }
    }

    public void OnViewThumbnail()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.ShowThumbnail(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to open media (thumbnail)", e);
        }
    }

    public void OnFolderMove()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.StartMoveAction(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to perform media folder move", e);
        }
    }

    public void OnFolderAdd()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.StartAddToFolderAction(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to perform media add to folder", e);
        }
    }

    public void OnFolderManager()
    {
        if (MediaOpenResources == null || MediaToShow == null)
            return;

        try
        {
            MediaOpenResources.StartManageFoldersAction(MediaToShow);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to perform media folder manager", e);
        }
    }

    public void SelectItem()
    {
        if (AllowSelection)
            Selected = true;
    }

    public void ReportDisplaySize(double sizeWidth, double sizeHeight)
    {
        lastWidth = (int)sizeWidth;
        lastHeight = (int)sizeHeight;

        if (AutoThumbnailSize)
            CalculateThumbnailSize();
    }

    public Point GetImageSpacePoint(double controlRelativeX, double controlRelativeY)
    {
        if (OnRequestImageSpacePoint == null)
            return new Point(-1, -1);

        return OnRequestImageSpacePoint(controlRelativeX, controlRelativeY);
    }

    public void SetRenderHook(IRenderHook? hook)
    {
        OnUpdateRenderHook?.Invoke(hook);
    }

    /// <summary>
    ///   Report by the renderer that the pan/zoom changed
    /// </summary>
    public void OnPanZoomChanged()
    {
    }

    public void ResetPan()
    {
        OnRequestPanReset?.Invoke(this, EventArgs.Empty);
    }

    public void CalculateThumbnailSize()
    {
        if (lastWidth <= 0 || lastHeight <= 0)
            return;

        // Thumbnail threshold is 400 in width
        ShowingThumbnail = lastWidth < 400;
    }

    public void ForceRedraw()
    {
        _ = ForceRedrawAsync();
    }

    public async Task ForceRedrawAsync()
    {
        // We trigger draw quests by forcing a property change the renderer listens for
        await Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(CurrentFrameBitmap)));
    }

    public void Dispose()
    {
        Task? loadTask;
        IVisualMediaSource? media;

        lock (taskLock)
        {
            if (disposed)
                return;

            disposed = true;
            animationTokenSource?.Cancel();
            animationTokenSource?.Dispose();
            animationTokenSource = null;

            loadTask = imageLoadTask;
            imageLoadTask = null;
            media = MediaToShow;
        }

        // The load task may still be using the media source. Waiting here blocks the UI thread, while disposing the
        // source immediately would race with the load. Finish clean-up asynchronously once the task has stopped.
        _ = DisposeAfterLoadTaskAsync(loadTask, media);
    }

    private async Task DisposeAfterLoadTaskAsync(Task? loadTask, IVisualMediaSource? media)
    {
        if (loadTask != null)
        {
            try
            {
                await loadTask.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger?.LogInformation(e, "Image load task stopped while disposing media viewer");
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (taskLock)
            {
                MediaOpenResources = null;
                media?.Dispose();
                if (ReferenceEquals(MediaToShow, media))
                    MediaToShow = null;

                CurrentFrameBitmap?.Dispose();
                CurrentFrameBitmap = null;
                OnSelectionChanged = null;
                OnDisplayedFrameChanged = null;
            }
        });
    }

    private void StartLoadingTask()
    {
        lock (taskLock)
        {
            if (disposed)
                return;

            animationTokenSource?.Cancel();
            animationTokenSource = new CancellationTokenSource();
            var token = animationTokenSource.Token;

            imageLoadTask = WaitForPreviousTaskIfNeeded().ContinueWith(_ => LoadMedia(token)).Unwrap();
        }
    }

    private async Task WaitForPreviousTaskIfNeeded()
    {
        Task? loadTask;
        lock (taskLock)
        {
            loadTask = imageLoadTask;
            imageLoadTask = null;
        }

        if (loadTask is { IsCompleted: false })
        {
            // Should wait for the previous load to finish
            logger?.LogWarning("Previous image load task still running, need to wait for it");
            try
            {
                await loadTask;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "Failed to wait for previous image load task");
            }
        }
    }

    private async Task LoadMedia(CancellationToken token)
    {
        var media = MediaToShow;

        // If already invisible, don't load (for example, when scrolling very fast)
        try
        {
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!IsVisible)
            return;

        if (media == null || disposed || token.IsCancellationRequested)
            return;

        // Set background icon if it is a folder or collection
        if (MediaToShow is ServerMediaSource serverSource)
        {
            if (serverSource.Info.IsFolder)
            {
                BackgroundSource = EmbeddedResourceImage.GetFolderIconConverted();

                // Folders do not show any images for now
                CurrentFrameBitmap = null;
                return;
            }

            if (serverSource.Info.IsCollection)
            {
                BackgroundSource = EmbeddedResourceImage.GetCollectionIconConverted();
            }
            else
            {
                BackgroundSource = null;
            }
        }
        else
        {
            BackgroundSource = null;
        }

        var wantedStatus =
            ShowingThumbnail ? IVisualMediaSource.LoadType.Thumbnail : IVisualMediaSource.LoadType.FullSize;

        if (media.LoadStatus == wantedStatus)
        {
            await LoadCurrentFrameForDisplay(media, token).ConfigureAwait(false);
            return;
        }

        // logger?.LogDebug("Loading happening on Thread: {Id}", Thread.CurrentThread.ManagedThreadId);

        // Need to load the media

        try
        {
            if (wantedStatus == IVisualMediaSource.LoadType.Thumbnail)
            {
                await media.RequestLoadThumbnail().ConfigureAwait(false);
            }
            else
            {
                await media.RequestLoadFull().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested)
                return;

            logger?.LogError(e, "Failed to load media for view");
            await Dispatcher.UIThread.InvokeAsync(UnloadDisplay);
            return;
        }

        await LoadCurrentFrameForDisplay(media, token).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(HasAudio));
            OnPropertyChanged(nameof(PlayAudio));
        });
    }

    private async Task LoadCurrentFrameForDisplay(IVisualMediaSource media, CancellationToken token)
    {
        // This runs on the background thread so that the bitmap creation doesn't lag the UI thread
        try
        {
            // Need to cancel this operation if disposed
            // TODO: should this be a waitable task on dispose?
            if (disposed || token.IsCancellationRequested)
                return;

            var frame = await media.GetCurrentFrameAsync(token).ConfigureAwait(false);

            var avaloniaBitmap = CurrentFrameBitmap;

            bool canReuse = avaloniaBitmap != null &&
                            avaloniaBitmap.PixelSize.Width == (int)frame.Width &&
                            avaloniaBitmap.PixelSize.Height == (int)frame.Height;

            if (avaloniaBitmap != null && frame.HasAlpha && avaloniaBitmap.Format != PixelFormats.Rgba8888)
                canReuse = false;

            if (avaloniaBitmap != null && !frame.HasAlpha && avaloniaBitmap.Format != PixelFormats.Rgb24)
                canReuse = false;

            if (avaloniaBitmap == null || !canReuse)
            {
                // Create a new bitmap on the background thread
                avaloniaBitmap = CustomImageControl.CreateBitmap(frame);

                // And only swap on the main thread to reduce lag
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (disposed || token.IsCancellationRequested)
                        return;

                    var old = CurrentFrameBitmap;
                    CurrentFrameBitmap = avaloniaBitmap;
                    old?.Dispose();
                });
            }
            else
            {
                if (disposed || token.IsCancellationRequested)
                    return;

                // Can reuse existing bitmap
                CustomImageControl.UpdateFrame(avaloniaBitmap, frame);

                // Notify on the main thread that the pixels changed
                await ForceRedrawAsync().ConfigureAwait(false);
            }

            if (media.IsAnimated)
            {
                if (disposed || token.IsCancellationRequested)
                    return;

                // Trigger frame advance once time to show the next frame
                _ = Task.Run(() => OnFrameTimeAdvance(media.GetNextFrameTime(), media, token), token);
            }

            OnDisplayedFrameChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested)
                return;

            logger?.LogError(e, "Failed to get current frame from media");
            await Dispatcher.UIThread.InvokeAsync(UnloadDisplay);
        }
    }

    private async Task OnFrameTimeAdvance(TimeSpan nextFrameTime, IVisualMediaSource media, CancellationToken token)
    {
        // If media changed cancel this callback
        if (token.IsCancellationRequested || CurrentFrameBitmap == null || MediaToShow != media || disposed)
            return;

        if (targetFrameTime == 0)
            targetFrameTime = Stopwatch.GetTimestamp();

        var effectiveNextFrameTime = nextFrameTime;
        if (effectiveNextFrameTime < TimeSpan.FromMilliseconds(10))
            effectiveNextFrameTime = TimeSpan.FromMilliseconds(10);

        targetFrameTime += (long)(effectiveNextFrameTime.TotalSeconds * Stopwatch.Frequency);

        var now = Stopwatch.GetTimestamp();
        var waitTime = (double)(targetFrameTime - now) / Stopwatch.Frequency;

        if (waitTime > 0)
        {
            // Subtract a tiny bit to account for Task.Delay and other overhead
            waitTime -= 0.001;

            if (waitTime > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitTime), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        else if (waitTime < -0.1)
        {
            // If we are more than 100ms behind, reset the target time to catch up
            targetFrameTime = now;
        }

        if (disposed || token.IsCancellationRequested)
            return;

        // If this is not currently visible, CurrentFrameBitMap is set to null, so that cancels this task

        try
        {
            // Or if media is unloaded to not get an error
            if (MediaToShow == null || MediaToShow.LoadStatus == IVisualMediaSource.LoadType.None)
                return;

            await media.AdvanceFrameAsync(token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested)
                logger?.LogError(e, "Failed to advance frame");
            return;
        }

        // If media changed cancel this callback
        if (token.IsCancellationRequested || CurrentFrameBitmap == null || MediaToShow != media || disposed)
            return;

        await LoadCurrentFrameForDisplay(media, token).ConfigureAwait(false);
    }

    private void UnloadDisplay()
    {
        CurrentFrameBitmap?.Dispose();
        CurrentFrameBitmap = null;
    }

    private void GetBrushes()
    {
        if (Application.Current!.TryGetResource("ImageBackground", null, out var raw) && raw is IBrush brush)
        {
            normalBackground = brush;
        }

        if (Application.Current.TryGetResource("SelectedImage", null, out raw) && raw is IBrush brush2)
        {
            selectedBackground = brush2;
        }
    }
}
