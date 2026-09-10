using System;
using System.Linq;
using Avalonia;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using DualView.GUI.Models;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class ImportSectionControl : UserControl
{
    private const double DragStartThreshold = 8;

    private static readonly DataFormat<ImportMediaDragData> MediaDragFormat =
        DataFormat.CreateInProcessFormat<ImportMediaDragData>("DualView.ImportMedia");

    private bool expandContent;
    private ImportSectionViewModel? dataContextViewModel;
    private Point dragStartPoint;
    private MediaViewer? dragSource;
    private PointerPressedEventArgs? dragPressedEvent;
    private bool dragInProgress;
    private MediaViewerViewModel? dragTargetViewModel;

    public ImportSectionControl()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ApplyExpandedLayout();
        DataContextChanged += OnDataContextChanged;

        AddHandler(PointerPressedEvent, OnDragPointerPressed, RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, OnSelectionPointerPressed, RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
    }

    /// <summary>
    ///   When set, this takes up all the space it wants. So this is used when popped out to do a custom layout
    ///  that is completely tall.
    /// </summary>
    public bool ExpandContent
    {
        get => expandContent;
        set
        {
            if (expandContent == value)
                return;

            expandContent = value;
            var verticalAlignment = value
                ? VerticalAlignment.Stretch
                : VerticalAlignment.Top;
            CollectionLayout.VerticalAlignment = verticalAlignment;
            ImagesLayout.VerticalAlignment = verticalAlignment;
            ApplyImageSizing();

            if (!value)
                return;

            ApplyExpandedLayout();
        }
    }

    private void ApplyExpandedLayout()
    {
        if (!expandContent)
            return;

        CollectionLayout.VerticalAlignment = VerticalAlignment.Stretch;
        ImagesLayout.VerticalAlignment = VerticalAlignment.Stretch;
        ImageContentLayout.VerticalAlignment = VerticalAlignment.Stretch;
        CollectionLayout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        FolderPickerControl.ClearValue(HeightProperty);
        FolderPickerControl.MinHeight = 305;
        ApplyImageSizing();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (dataContextViewModel != null)
            dataContextViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        dataContextViewModel = DataContext as ImportSectionViewModel;
        if (dataContextViewModel != null)
            dataContextViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyImageSizing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportSectionViewModel.PreviewScrollViewerHeight))
            ApplyImageSizing();
    }

    private void ApplyImageSizing()
    {
        ImagesScrollViewer.Height = expandContent || dataContextViewModel == null
            ? double.NaN
            : dataContextViewModel.PreviewScrollViewerHeight;
        ImagesScrollViewer.MinHeight = 0;
        ImagesScrollViewer.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private void OnTargetNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: null, Text: var text } &&
            DataContext is ImportSectionViewModel viewModel)
        {
            _ = viewModel.LoadTargetNameSuggestionsAsync(text ?? string.Empty);
        }
    }

    private void OnSelectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            DataContext is not ImportSectionViewModel viewModel)
            return;

        var mediaViewer = FindMediaViewer(e.Source as Control);
        if (mediaViewer?.DataContext is not MediaViewerViewModel item || !item.Selected)
            return;

        var itemIndex = viewModel.Media.IndexOf(item);
        var startIndex = itemIndex - 1;
        while (startIndex >= 0 && !viewModel.Media[startIndex].Selected)
            --startIndex;

        if (startIndex < 0)
        {
            var endIndex = itemIndex + 1;
            while (endIndex < viewModel.Media.Count && !viewModel.Media[endIndex].Selected)
                ++endIndex;

            if (endIndex < viewModel.Media.Count)
            {
                for (var index = itemIndex; index <= endIndex; ++index)
                    viewModel.Media[index].Selected = true;

                return;
            }

            startIndex = 0;
        }
        else
        {
            ++startIndex;
        }

        for (var index = startIndex; index <= itemIndex; ++index)
            viewModel.Media[index].Selected = true;
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed || e.Source is not Control source)
            return;

        dragSource = FindMediaViewer(source);
        if (dragSource == null)
            return;

        dragStartPoint = e.GetPosition(this);
        dragPressedEvent = e;
        dragInProgress = false;
    }

    private async void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragInProgress || dragSource == null || dragPressedEvent == null || !e.Properties.IsLeftButtonPressed ||
            DataContext is not ImportSectionViewModel viewModel)
            return;

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - dragStartPoint.X) < DragStartThreshold &&
            Math.Abs(currentPoint.Y - dragStartPoint.Y) < DragStartThreshold)
        {
            return;
        }

        if (dragSource.DataContext is not MediaViewerViewModel mediaViewModel ||
            mediaViewModel.MediaToShow is not ServerMediaSource source)
            return;

        var mediaIds = viewModel.Media.Where(media => media.Selected)
            .Select(media => ((ServerMediaSource)media.MediaToShow!).ServerId)
            .ToList();
        if (!mediaIds.Contains(source.ServerId))
            mediaIds.Add(source.ServerId);

        var draggedViewers = viewModel.Media
            .Where(media => mediaIds.Contains(((ServerMediaSource)media.MediaToShow!).ServerId))
            .ToList();
        foreach (var draggedViewer in draggedViewers)
            draggedViewer.IsDragging = true;

        dragInProgress = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(MediaDragFormat,
                new ImportMediaDragData(viewModel, mediaIds)));
            await DragDrop.DoDragDropAsync(dragPressedEvent, transfer, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            foreach (var draggedViewer in draggedViewers)
                draggedViewer.IsDragging = false;

            dragSource = null;
            dragPressedEvent = null;
            dragInProgress = false;
        }
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearDragTarget();
        dragSource = null;
        dragPressedEvent = null;
        dragInProgress = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(MediaDragFormat) || DataContext is not ImportSectionViewModel)
        {
            ClearDragTarget();
            return;
        }

        var target = FindMediaViewer(e.Source as Control);
        if (target?.DataContext is not MediaViewerViewModel targetViewModel)
        {
            ClearDragTarget();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.DataTransfer.TryGetValue(MediaDragFormat) is { } dragData &&
            dragData.SourceSection == DataContext &&
            dragData.MediaIds.Contains(GetMediaId(targetViewModel)))
        {
            ClearDragTarget();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertAfter = e.GetPosition(target).X > target.Bounds.Width / 2;
        ClearDragTarget();
        dragTargetViewModel = targetViewModel;
        dragTargetViewModel.SetDragTarget(insertAfter);

        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDragTarget();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ImportSectionViewModel viewModel ||
            e.DataTransfer.TryGetValue(MediaDragFormat) is not { } dragData)
        {
            return;
        }

        ClearDragTarget();

        var target = FindMediaViewer(e.Source as Control);
        if (target?.DataContext is MediaViewerViewModel dropTargetViewModel &&
            ReferenceEquals(dragData.SourceSection, viewModel) &&
            dropTargetViewModel.MediaToShow is ServerMediaSource targetSource &&
            dragData.MediaIds.Contains(targetSource.ServerId))
        {
            // This should trigger when drag tries to drop onto the same items that are being dragged
            e.Handled = true;
            return;
        }

        var index = target?.DataContext is MediaViewerViewModel targetViewModel
            ? viewModel.Media.IndexOf(targetViewModel)
            : viewModel.Media.Count;
        if (target != null)
        {
            var point = e.GetPosition(target);
            if (point.X > target.Bounds.Width / 2)
                ++index;
        }

        await viewModel.MoveMediaAsync(dragData, index,
            e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Handled = true;
    }

    private void ClearDragTarget()
    {
        dragTargetViewModel?.ClearDragTarget();
        dragTargetViewModel = null;
    }

    private static long GetMediaId(MediaViewerViewModel viewer)
    {
        return ((ServerMediaSource)viewer.MediaToShow!).ServerId;
    }

    private static MediaViewer? FindMediaViewer(Control? control)
    {
        while (control != null)
        {
            if (control is MediaViewer mediaViewer)
                return mediaViewer;
            control = control.Parent as Control;
        }

        return null;
    }
}
