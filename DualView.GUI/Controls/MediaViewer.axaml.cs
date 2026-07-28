using System;
using System.Numerics;
using DualView.GUI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DualView.GUI.Controls;

public partial class MediaViewer : UserControl
{
    private bool reportedSize;

    private bool middleClickDown;
    private Point middleClickDownStart;

    public MediaViewer()
    {
        InitializeComponent();

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        SizeChanged += CheckViewerSize;
        PointerMoved += OnPointerMoved;
        PointerWheelChanged += OnScrolledEvent;

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        middleClickDown = false;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MediaViewerViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MediaViewerViewModel.CurrentFrameBitmap))
                {
                    // Need to force the custom image control to redraw for animated images to work
                    // InvalidateVisual();
                    MainDisplay.InvalidateVisual();
                }
                else if (args.PropertyName == nameof(MediaViewerViewModel.MediaToShow))
                {
                    // Reset the zoom and pan
                    MainDisplay.RenderScale = 1;
                    MainDisplay.RenderOffset = new Vector2(0, 0);
                }
            };

            vm.OnRequestImageSpacePoint += ConvertCoordinates;
            vm.OnRequestBrushPreviewTarget += ReturnBrushPreviewTarget;
            vm.OnUpdateRenderHook += SetRenderHook;
            vm.OnRequestPanReset += ResetPan;
        }
    }

    private void ResetPan(object? sender, EventArgs e)
    {
        MainDisplay.RenderOffset = new Vector2(0, 0);
        MainDisplay.RenderScale = 1;
    }

    // TODO: need to verify with an animated image that rendering stops when offscreen (this seems to be the case, but might need more verification)

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is MediaViewerViewModel vm)
        {
            // Report initial size
            if (!reportedSize)
            {
                reportedSize = true;
                var size = MainDisplay.Bounds;
                vm.ReportDisplaySize(size.Width, size.Height);
            }
        }
    }

    private void CheckViewerSize(object? sender, SizeChangedEventArgs e)
    {
        var size = MainDisplay.Bounds;

        if (DataContext is MediaViewerViewModel vm)
        {
            vm.ReportDisplaySize(size.Width, size.Height);
            reportedSize = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!e.Properties.IsMiddleButtonPressed)
        {
            middleClickDown = false;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsMiddleButtonPressed)
        {
            if (!middleClickDown)
            {
                middleClickDown = true;
                middleClickDownStart = e.GetPosition(MainDisplay);
            }
        }
        else
        {
            middleClickDown = false;
        }

        // We don't want to react on right clicks
        if (!e.Properties.IsLeftButtonPressed)
            return;

        // Check if the DataContext is our ViewModel
        if (DataContext is MediaViewerViewModel vm)
        {
            // Handle Double Click
            if (e.ClickCount == 2)
            {
                if (vm.OnDoubleClick())
                {
                    e.Handled = true;

                    // Allow resetting the selection state with the second press
                    // return;
                }
            }

            if (vm.AllowSelection)
            {
                // Toggle the selected state
                vm.Selected = !vm.Selected;

                e.Handled = true;
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is MediaViewerViewModel vm && vm.AllowPanning)
        {
            if (middleClickDown)
            {
                var newPos = e.GetPosition(MainDisplay);

                var delta = newPos - middleClickDownStart;
                MainDisplay.RenderOffset += new Vector2((float)delta.X, (float)delta.Y);
                middleClickDownStart = newPos;

                vm.OnPanZoomChanged();
            }
        }
    }

    private void OnScrolledEvent(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MediaViewerViewModel vm || !vm.AllowPanning)
            return;

        // Zoom *towards the mouse position*:
        // Keep the point under the cursor stable by adjusting RenderOffset as scale changes.
        var oldScale = MainDisplay.RenderScale;
        if (oldScale <= 0.00001f)
            oldScale = 0.00001f;

        // Wheel speed: Shift = slower, no Shift = faster, and Control = even faster
        // Using multiplicative zoom feels nicer than additive and avoids crossing zero.
        double zoomBase = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1.03 : 1.15;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            zoomBase = 1.35;

        float newScale = (float)(oldScale * Math.Pow(zoomBase, e.Delta.Y));

        // Clamp to something sensible
        newScale = Math.Clamp(newScale, 0.05f, 60f);

        // If clamping results in no effective change, bail early
        if (Math.Abs(newScale - oldScale) < 0.000001f)
            return;

        var mouse = e.GetPosition(MainDisplay);
        var bounds = MainDisplay.Bounds;

        var center = new Vector2((float)(bounds.Width / 2.0), (float)(bounds.Height / 2.0));
        var mouseFromCenter = new Vector2((float)mouse.X, (float)mouse.Y) - center;

        var oldOffset = MainDisplay.RenderOffset;

        // r = newScale / oldScale
        // newOffset = oldOffset + (1 - r) * (mouseFromCenter - oldOffset)
        // This keeps the content under the cursor fixed while zooming.
        var r = newScale / oldScale;
        var newOffset = oldOffset + (1f - r) * (mouseFromCenter - oldOffset);

        MainDisplay.RenderScale = newScale;
        MainDisplay.RenderOffset = newOffset;

        vm.OnPanZoomChanged();
        e.Handled = true;
    }

    private Point ConvertCoordinates(double controlRelativeX, double controlRelativeY)
    {
        // Translate the point from the MediaViewer control's space to MainDisplay's space
        var pointInMediaViewer = new Point(controlRelativeX, controlRelativeY);
        var pointInMainDisplay = MainDisplay.TranslatePoint(pointInMediaViewer, MainDisplay);

        if (pointInMainDisplay == null)
        {
            // Fallback: assume the point is already relative to MainDisplay (or some error)
            return MainDisplay.PointToImageSpace(controlRelativeX, controlRelativeY);
        }

        return MainDisplay.PointToImageSpace(pointInMainDisplay.Value.X, pointInMainDisplay.Value.Y);
    }

    private IBrushPreviewTarget? ReturnBrushPreviewTarget()
    {
        return MainDisplay;
    }

    private void SetRenderHook(IRenderHook? hook)
    {
        MainDisplay.RenderHook = hook;
    }
}
