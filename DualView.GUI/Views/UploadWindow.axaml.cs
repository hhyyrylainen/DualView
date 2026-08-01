using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Views;

public partial class UploadWindow : Window
{
    public UploadWindow()
    {
        InitializeComponent();
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select files to upload",
            AllowMultiple = true,
        });

        if (files.Any() && DataContext is UploadWindowViewModel vm)
        {
            vm.AddFiles(files.Select(f => f.Path.LocalPath).ToArray());
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
