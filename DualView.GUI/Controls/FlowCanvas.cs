using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace DualView.GUI.Controls;

/// <summary>
///   Handles displaying Flow nodes and allows panning and zooming.
/// </summary>
public class FlowCanvas : Panel
{
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<FlowCanvas, double>(nameof(Zoom), 1.0,
            defaultBindingMode: BindingMode.TwoWay);

    private bool isUpdating;
    private Point lastMousePosition;
    private bool isPanning;

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public static readonly StyledProperty<Point> OffsetProperty =
        AvaloniaProperty.Register<FlowCanvas, Point>(nameof(Offset), new Point(0, 0),
            defaultBindingMode: BindingMode.TwoWay);

    public Point Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public FlowCanvas()
    {
        ClipToBounds = false;
        Background = Brushes.Transparent;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(finalSize));
        }

        UpdateTransform();
        return finalSize;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled)
            return;

        var properties = e.GetCurrentPoint(this).Properties;

        // We want to reserve left mouse for interacting so only middle mouse pans
        if (properties.IsMiddleButtonPressed)
        {
            isPanning = true;
            lastMousePosition = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (isPanning)
        {
            isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (isPanning)
        {
            var currentPosition = e.GetPosition(this);
            var delta = currentPosition - lastMousePosition;
            Offset += delta;
            lastMousePosition = currentPosition;
            // No need to call UpdateTransform here if we handle it in OnPropertyChanged
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var mousePos = e.GetPosition(this);

        var oldZoom = Zoom;
        var zoomFactor = Math.Pow(1.1, e.Delta.Y);
        var newZoom = oldZoom * zoomFactor;

        // Snap to 100% if close
        // TODO: make the zoom factor change in steps that result in hitting 100 percent
        if (Math.Abs(newZoom - 1.0) < 0.05)
            newZoom = 1.0;

        // Limit zoom
        if (newZoom < 0.1)
            newZoom = 0.1;
        if (newZoom > 10.0)
            newZoom = 10.0;

        zoomFactor = newZoom / oldZoom;

        isUpdating = true;
        try
        {
            // The point under the mouse should stay under the mouse
            Offset = mousePos - (mousePos - Offset) * zoomFactor;
            Zoom = newZoom;
        }
        finally
        {
            isUpdating = false;
        }

        UpdateTransform();
        e.Handled = true;
    }

    private void UpdateTransform()
    {
        var transform =
            new MatrixTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Offset.X, Offset.Y));
        foreach (var child in Children)
        {
            child.RenderTransform = transform;
            child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!isUpdating && (change.Property == ZoomProperty || change.Property == OffsetProperty))
        {
            UpdateTransform();
        }
    }
}
