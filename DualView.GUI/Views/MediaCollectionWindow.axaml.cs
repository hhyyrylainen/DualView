using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class MediaCollectionWindow : Window
{
    public MediaCollectionWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
}
