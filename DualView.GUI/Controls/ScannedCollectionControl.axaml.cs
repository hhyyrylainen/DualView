using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class ScannedCollectionControl : UserControl
{
    public ScannedCollectionControl()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnSelectionPointerPressed, RoutingStrategies.Bubble, true);
    }

    private void OnTargetNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: null, Text: var text } &&
            DataContext is ScannedCollectionViewModel viewModel)
        {
            _ = viewModel.LoadTargetNameSuggestionsAsync(text ?? string.Empty);
        }
    }

    private void OnSelectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) ||
            DataContext is not ScannedCollectionViewModel viewModel)
            return;

        var mediaViewer = FindMediaViewer(e.Source as Control);
        if (mediaViewer?.DataContext is not MediaViewerViewModel item || !item.Selected)
            return;

        var itemIndex = viewModel.Media.IndexOf(item);
        if (itemIndex < 0)
            return;

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

    private async void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScannedCollectionViewModel viewModel)
            return;

        // Make sure this is definitely up to date before accepting
        viewModel.TargetName = TargetNameInput.Text ?? string.Empty;
        await viewModel.AcceptAsync();
    }
}
