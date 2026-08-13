using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using Vector = Avalonia.Vector;

namespace DualView.GUI.Controls;

public class CustomImageControl : Control, IBrushPreviewTarget
{
    // Define a property to bind from XAML/ViewModel
    public static readonly DirectProperty<CustomImageControl, WriteableBitmap?> SourceProperty =
        AvaloniaProperty.RegisterDirect<CustomImageControl, WriteableBitmap?>(
            nameof(Source), o => o.Source, (o, v) => o.Source = v);

    public static readonly DirectProperty<CustomImageControl, bool> IsVisibleInViewportProperty =
        AvaloniaProperty.RegisterDirect<CustomImageControl, bool>(
            nameof(IsVisibleInViewport), o => o.IsVisibleInViewport);

    public static readonly DirectProperty<CustomImageControl, Bitmap?> ExtraOverlayImageProperty =
        AvaloniaProperty.RegisterDirect<CustomImageControl, Bitmap?>(
            nameof(ExtraOverlayImage), o => o.ExtraOverlayImage, (o, v) => o.ExtraOverlayImage = v);

    public static readonly DirectProperty<CustomImageControl, Bitmap?> BackgroundSourceProperty =
        AvaloniaProperty.RegisterDirect<CustomImageControl, Bitmap?>(
            nameof(BackgroundSource), o => o.BackgroundSource, (o, v) => o.BackgroundSource = v);

    /// <summary>
    ///   Used to detect when this is actually visible in the viewport and only load data when needed.
    /// </summary>
    public bool IsVisibleInViewport
    {
        get;
        private set
        {
            if (field == value)
                return;

            SetAndRaise(IsVisibleInViewportProperty, ref field, value);
        }
    }

    public WriteableBitmap? Source
    {
        get;
        set
        {
            SetAndRaise(SourceProperty, ref field, value);

            // Force a redrawing when the bitmap changes
            InvalidateVisual();
        }
    }

    public Bitmap? ExtraOverlayImage
    {
        get;
        set
        {
            SetAndRaise(ExtraOverlayImageProperty, ref field, value);
            InvalidateVisual();
        }
    }

    public Bitmap? BackgroundSource
    {
        get;
        set
        {
            SetAndRaise(BackgroundSourceProperty, ref field, value);
            InvalidateVisual();
        }
    }

    public Vector RenderOffset
    {
        get;
        set
        {
            if (value == field)
                return;
            field = value;
            InvalidateVisual();
        }
    } = new(0, 0);

    public double RenderScale
    {
        get;
        set
        {
            if (Math.Abs(value - field) < 0.00001)
                return;

            field = value;
            InvalidateVisual();
        }
    } = 1;

    //
    // Cursor preview on the image
    //
    /// <summary>
    ///   Preview cursor position, in *control* coordinates.
    /// </summary>
    public Point? BrushPreviewPosition
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            InvalidateVisual();
        }
    }

    public double BrushPreviewRadiusInImagePixels
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.00001)
                return;

            field = value;
            InvalidateVisual();
        }
    } = 8;

    public bool BrushIsSquare
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            InvalidateVisual();
        }
    }

    public bool ShowBrushPreview
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            InvalidateVisual();
        }
    }

    // An attached control has not been proven to be in the viewport until Avalonia reports its effective
    // viewport. Start empty so off-screen controls cannot begin loading while waiting for that report.
    private Rect lastViewport;

    public CustomImageControl()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The control may be reused after being detached. Do not carry a previous in-viewport state into the
        // new visual tree before the new effective viewport has been calculated.
        lastViewport = new();
        IsVisibleInViewport = false;

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        IsVisibleInViewport = false;

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.IsVisibleProperty)
        {
            UpdateVisibility();
        }
    }

    public static unsafe void UpdateFrame(WriteableBitmap target, MagickImage frame)
    {
        using (var lockedBitmap = target.Lock())
        {
            using var sourcePixels = frame.GetPixelsUnsafe();

            // Manual math based on the expected format
            var sourceSize = (int)frame.Width * (int)frame.Height * (frame.HasAlpha ? 4 : 3);
            var destinationSize = lockedBitmap.RowBytes * target.PixelSize.Height;

            if (sourceSize != destinationSize)
            {
                // We need to fall back on line-by-line copy
                PerformRowByRowCopy(lockedBitmap, frame);
                return;
            }

            var source = sourcePixels.GetAreaPointer(0, 0, frame.Width, frame.Height);

            Buffer.MemoryCopy(source.ToPointer(), lockedBitmap.Address.ToPointer(), destinationSize, sourceSize);
        }

        // Notify the control to redraw
        // (If bound to the 'Source' property in the control above, it happens automatically)
    }

    public static unsafe void UpdateFrame(MagickImage frame, IntPtr sourcePtr, uint stride)
    {
        if (frame.Width * 4 != stride)
            throw new Exception("Invalid stride for a direct copy");

        using var pixels = frame.GetPixelsUnsafe();
        byte* startPointer = (byte*)sourcePtr.ToPointer();

        var height = frame.Height;
        for (int y = 0; y < height; ++y)
        {
            var target = pixels.GetAreaPointer(0, y, frame.Width, 1);

            Buffer.MemoryCopy(startPointer + y * stride, target.ToPointer(), stride, stride);
        }
    }

    public static unsafe void UpdateFrameDirect(MagickImage frame, IntPtr sourcePtr, uint stride)
    {
        // This method copies all at once for maximum efficiency
        if (frame.Width * 4 != stride)
            throw new Exception("Invalid stride for a direct copy");

        var size = stride * frame.Height;

        using var pixels = frame.GetPixelsUnsafe();
        var target = pixels.GetAreaPointer(0, 0, frame.Width, frame.Height);

        Buffer.MemoryCopy(sourcePtr.ToPointer(), target.ToPointer(), size, size);
    }

    public static void UpdateFrameRowByRow(WriteableBitmap target, MagickImage frame)
    {
        using var lockedBitmap = target.Lock();
        PerformRowByRowCopy(lockedBitmap, frame);
    }

    public static unsafe void ReadBitmapPixel(ILockedFramebuffer lockedBitMap, int x, int y, out byte r, out byte g,
        out byte b, out byte a)
    {
        bool alpha = lockedBitMap.Format == PixelFormat.Rgba8888;

        int pixelOffset;
        if (alpha)
        {
            pixelOffset = lockedBitMap.RowBytes * y + x * 4;
        }
        else
        {
            pixelOffset = lockedBitMap.RowBytes * y + x * 3;
        }

        var address = lockedBitMap.Address + pixelOffset;

        byte* ptr = (byte*)address;

        // Address is IntPtr which we want to read

        r = ptr[0];
        g = ptr[1];
        b = ptr[2];

        a = alpha ? ptr[3] : (byte)255;
    }

    private static unsafe void PerformRowByRowCopy(ILockedFramebuffer lockedBitmap, MagickImage frame)
    {
        // Per row copy
        int rows = lockedBitmap.Size.Height;

        using var sourcePixels = frame.GetPixelsUnsafe();
        var destinationSize = lockedBitmap.RowBytes;
        var sourceSize = (int)frame.Width * (frame.HasAlpha ? 4 : 3);
        if (sourceSize != destinationSize)
        {
            // Allow slight overshoot if the destination has padding
            if (sourceSize > destinationSize || sourceSize < destinationSize - 15)
                throw new Exception("Invalid bitmap size (logic error in copying)");
        }

        // We can get all source pixels as contiguous memory for efficiency outside the loop
        var source = sourcePixels.GetAreaPointer(0, 0, frame.Width, frame.Height);

        for (int i = 0; i < rows; ++i)
        {
            var sourceRow = source + sourceSize * i;

            var targetPtr = lockedBitmap.Address + lockedBitmap.RowBytes * i;

            Buffer.MemoryCopy(sourceRow.ToPointer(), targetPtr.ToPointer(), destinationSize, sourceSize);
        }
    }

    public IRenderHook? RenderHook
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            InvalidateVisual();
        }
    }

    public static WriteableBitmap CreateBitmap(MagickImage frame)
    {
        MagickImage workingFrame = frame;
        bool cloned = false;

        // Ensure the image is in a format Avalonia can handle directly from the buffer.
        // We expect TrueColor (RGB, 3 bytes) or TrueColorAlpha (RGBA, 4 bytes) with 8-bit depth.
        // Grayscale, Palette, or 16-bit images need conversion.
        if (frame.Depth != 8 ||
            frame.ColorSpace != ColorSpace.sRGB ||
            (frame.HasAlpha && frame.ColorType != ColorType.TrueColorAlpha) ||
            (!frame.HasAlpha && frame.ColorType != ColorType.TrueColor))
        {
            workingFrame = (MagickImage)frame.Clone();
            cloned = true;

            if (workingFrame.Depth != 8)
                workingFrame.Depth = 8;

            if (workingFrame.ColorSpace != ColorSpace.sRGB)
                workingFrame.ColorSpace = ColorSpace.sRGB;

            if (workingFrame.HasAlpha)
            {
                if (workingFrame.ColorType != ColorType.TrueColorAlpha)
                    workingFrame.ColorType = ColorType.TrueColorAlpha;
            }
            else
            {
                if (workingFrame.ColorType != ColorType.TrueColor)
                    workingFrame.ColorType = ColorType.TrueColor;
            }
        }

        try
        {
            // The underlying ImageMagick buffer changes depending on if it has alpha or not
            var avaloniaFormat = workingFrame.HasAlpha ? PixelFormats.Rgba8888 : PixelFormats.Rgb24;

            // TODO: a proper DPI value?
            var dpi = new Vector(96, 96);

            // Pixel size should be the raw pixel size
            var size = new PixelSize((int)workingFrame.Width, (int)workingFrame.Height);

            using var sourcePixels = workingFrame.GetPixelsUnsafe();
            var source = sourcePixels.GetAreaPointer(0, 0, workingFrame.Width, workingFrame.Height);

            // And thus the size here depends on if there is alpha or not
            int stride = (int)workingFrame.Width * (workingFrame.HasAlpha ? 4 : 3);

            var bitmap = new WriteableBitmap(avaloniaFormat, AlphaFormat.Unpremul, source, size, dpi, stride);

            return bitmap;
        }
        finally
        {
            if (cloned)
                workingFrame.Dispose();
        }
    }

    public override void Render(DrawingContext context)
    {
        var background = BackgroundSource;
        var source = Source;
        if (source == null && background == null)
            return;

        var overlay = ExtraOverlayImage;
        var sourceSize = source?.Size ?? background!.Size;
        var destRect = GetImageDestinationRect(sourceSize);

        if (background != null)
        {
            var backgroundDestRect = GetImageDestinationRect(background.Size);
            context.DrawImage(background, new Rect(background.Size), backgroundDestRect);
        }

        if (source != null)
        {
            destRect = GetImageDestinationRect(source.Size);
            context.DrawImage(source, new Rect(source.Size), destRect);
        }

        if (overlay != null)
        {
            context.DrawImage(overlay, new Rect(overlay.Size), destRect);
        }

        // Brush preview
        if (ShowBrushPreview && BrushPreviewPosition is { } previewPosition)
        {
            double scaleX = destRect.Width / sourceSize.Width;
            double scaleY = destRect.Height / sourceSize.Height;
            double radius = BrushPreviewRadiusInImagePixels / 2 * Math.Min(scaleX, scaleY);

            var fill = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
            var pen = new Pen(Brushes.Red, 1);

            if (BrushIsSquare)
            {
                context.DrawRectangle(fill, pen,
                    new Rect(previewPosition.X - radius, previewPosition.Y - radius, radius * 2, radius * 2));
            }
            else
            {
                context.DrawEllipse(fill, pen, previewPosition, radius, radius);
            }
        }

        RenderHook?.Render(context, destRect, sourceSize);
    }

    public Point PointToImageSpace(double controlRelativeX, double controlRelativeY)
    {
        var source = Source ?? throw new InvalidOperationException("Cannot map coordinates without a source image.");

        var sourceSize = source.Size;
        var destRect = GetImageDestinationRect(sourceSize);

        double imageX = (controlRelativeX - destRect.X) / destRect.Width * sourceSize.Width;
        double imageY = (controlRelativeY - destRect.Y) / destRect.Height * sourceSize.Height;

        return new Point(imageX, imageY);
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        // If the effective viewport is empty, the control is completely outside the scroll area.
        lastViewport = e.EffectiveViewport;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool inViewport = lastViewport.Height > 0 && lastViewport.Width > 0;

        var window = TopLevel.GetTopLevel(this) as Window;
        bool windowVisible = window == null || (window.IsVisible && window.WindowState != WindowState.Minimized);

        IsVisibleInViewport = inViewport && windowVisible;
    }

    private Rect GetImageDestinationRect(Size imageSize)
    {
        var viewPort = new Rect(Bounds.Size);

        double baseScale = Math.Min(viewPort.Width / imageSize.Width, viewPort.Height / imageSize.Height);
        Size baseDestSize = imageSize * baseScale;

        double zoom = Math.Max(0.00001, RenderScale);
        Size zoomedDestSize = new(baseDestSize.Width * zoom, baseDestSize.Height * zoom);

        Rect destRect = viewPort.CenterRect(new Rect(zoomedDestSize));
        return destRect.Translate(RenderOffset);
    }
}

public interface IBrushPreviewTarget
{
    public Point? BrushPreviewPosition { get; set; }

    public double BrushPreviewRadiusInImagePixels { get; set; }

    public bool BrushIsSquare { get; set; }

    public bool ShowBrushPreview { get; set; }
}

public interface IRenderHook
{
    public void Render(DrawingContext context, Rect destRect, Size sourceSize);
}
