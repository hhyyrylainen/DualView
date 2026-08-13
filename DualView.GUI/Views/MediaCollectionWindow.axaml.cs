using DualView.GUI.ViewModels;
using Avalonia.Controls;

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
}
