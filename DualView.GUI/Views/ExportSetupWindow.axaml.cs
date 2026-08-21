using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Views;

public partial class ExportSetupWindow : Window
{
    public ExportSetupWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ExportSetupWindowViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
            viewModel.FolderPickerRequested += PickFolderAsync;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e) => Close();

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select export folder",
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
