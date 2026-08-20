using Avalonia.Controls;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Views;

public partial class ImportSectionWindow : Window
{
    public ImportSectionWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ImportSectionWindowViewModel viewModel)
            viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        Close();
    }
}
