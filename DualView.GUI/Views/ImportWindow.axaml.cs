using DualView.GUI.ViewModels;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using System.Threading.Tasks;

namespace DualView.GUI.Views;

public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ImportWindowViewModel viewModel)
            viewModel.RequestCopyToClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ImportWindowViewModel viewModel)
            viewModel.SearchOrCreate();
    }
}
