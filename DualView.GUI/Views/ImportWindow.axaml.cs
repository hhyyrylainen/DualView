using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace DualView.GUI.Views;

public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ImportWindowViewModel viewModel)
            viewModel.SearchOrCreate();
    }
}
