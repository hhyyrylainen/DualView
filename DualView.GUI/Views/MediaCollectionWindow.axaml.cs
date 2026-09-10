using System;
using DualView.GUI.ViewModels;
using DualView.GUI.Controls;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using DualView.GUI.Models;
using System.Linq;

namespace DualView.GUI.Views;

public partial class MediaCollectionWindow : Window
{
    private const double DragStartThreshold = 6;

    private static readonly DataFormat<CollectionMediaDragData> MediaDragFormat =
        DataFormat.CreateInProcessFormat<CollectionMediaDragData>("DualView.CollectionMedia");

    private Point dragStartPoint;
    private MediaViewer? dragSource;
    private PointerPressedEventArgs? dragPressedEvent;
    private bool dragInProgress;
    private MediaViewerViewModel? dragTargetViewModel;

    public MediaCollectionWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, OnDragPointerPressed, RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, true);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MediaCollectionWindowViewModel viewModel)
            viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        Close();
    }

    private void OnPairedImageModeClick(object? sender, RoutedEventArgs e)
    {
        // NOTE: this click handler is needed to get this to actually stick!
        if (DataContext is MediaCollectionWindowViewModel viewModel && sender is MenuItem menuItem)
            viewModel.SetPairedImageMode(menuItem.IsChecked);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            DataContext is not MediaCollectionWindowViewModel viewModel)
            return;

        var mediaViewer = FindMediaViewer(e.Source as Control);
        if (mediaViewer?.DataContext is not MediaViewerViewModel item || !item.Selected)
            return;

        var itemIndex = viewModel.CollectionItems.IndexOf(item);
        var startIndex = itemIndex - 1;
        while (startIndex >= 0 && !viewModel.CollectionItems[startIndex].Selected)
            --startIndex;

        ++startIndex;
        for (var index = startIndex; index <= itemIndex; ++index)
            viewModel.CollectionItems[index].Selected = true;
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed || e.Source is not Control source ||
            DataContext is not MediaCollectionWindowViewModel viewModel || !viewModel.CanReorderItems)
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
        if (dragInProgress || dragSource == null || dragPressedEvent == null ||
            !e.Properties.IsLeftButtonPressed || DataContext is not MediaCollectionWindowViewModel viewModel ||
            dragSource.DataContext is not MediaViewerViewModel mediaViewModel ||
            mediaViewModel.MediaToShow is not ServerMediaSource source)
            return;

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - dragStartPoint.X) < DragStartThreshold &&
            Math.Abs(currentPoint.Y - dragStartPoint.Y) < DragStartThreshold)
            return;

        var mediaIds = viewModel.CollectionItems.Where(item => item.Selected)
            .Select(item => (item.MediaToShow as ServerMediaSource)?.ServerId)
            .Where(id => id.HasValue).Select(id => id!.Value).ToList();
        if (!mediaIds.Contains(source.ServerId))
            mediaIds.Add(source.ServerId);

        var draggedViewers = viewModel.CollectionItems
            .Where(item => item.MediaToShow is ServerMediaSource source && mediaIds.Contains(source.ServerId))
            .ToList();
        foreach (var draggedViewer in draggedViewers)
            draggedViewer.IsDragging = true;

        dragInProgress = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(MediaDragFormat, new CollectionMediaDragData(mediaIds)));
            await DragDrop.DoDragDropAsync(dragPressedEvent, transfer, DragDropEffects.Move);
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
        if (!e.DataTransfer.Contains(MediaDragFormat) || DataContext is not MediaCollectionWindowViewModel viewModel ||
            !viewModel.CanReorderItems)
        {
            ClearDragTarget();
            return;
        }

        var target = FindMediaViewer(e.Source as Control);
        if (target?.DataContext is not MediaViewerViewModel targetViewModel ||
            e.DataTransfer.TryGetValue(MediaDragFormat) is not { } dragData ||
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
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDragTarget();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MediaCollectionWindowViewModel viewModel ||
            e.DataTransfer.TryGetValue(MediaDragFormat) is not { } dragData)
            return;

        ClearDragTarget();

        var target = FindMediaViewer(e.Source as Control);
        var targetViewModel = target?.DataContext as MediaViewerViewModel;
        if (targetViewModel == null || targetViewModel.MediaToShow is not ServerMediaSource targetSource ||
            dragData.MediaIds.Contains(targetSource.ServerId))
        {
            e.Handled = true;
            return;
        }

        var targetControl = target!;
        var targetPoint = e.GetPosition(targetControl);
        var insertAfter = targetPoint.X > targetControl.Bounds.Width / 2;
        viewModel.MoveMediaForReorder(dragData.MediaIds, targetSource.ServerId, insertAfter);
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
