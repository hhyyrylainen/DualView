using DualView.GUI.ViewModels;
using DualView.GUI.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class MediaCollectionWindow : Window
{
    public MediaCollectionWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, true);
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
