using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Views;

public partial class UploadWindow : Window
{
    public UploadWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, true);
        Closed += (s, e) => (DataContext as UploadWindowViewModel)?.RemoveAll();
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
            await vm.AddFilesAsync(files.Select(f => f.Path.LocalPath).ToArray());
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTargetNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: null, Text: var text } &&
            DataContext is UploadWindowViewModel viewModel)
        {
            _ = viewModel.LoadTargetNameSuggestionsAsync(text ?? string.Empty);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not UploadWindowViewModel vm)
            return;

        var paths = e.DataTransfer.Items
            .Select(item => item.TryGetRaw(DataFormat.File))
            .OfType<IStorageItem>()
            .Select(item => item.Path.LocalPath)
            .ToArray();

        if (paths.Length > 0)
            await vm.AddFilesAsync(paths);

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) || DataContext is not UploadWindowViewModel vm)
            return;

        var entry = FindUploadEntry(e.Source as Control);
        if (entry == null || !entry.IsSelected)
            return;

        // Handle shift selection of items
        var entryIndex = vm.FilesToUpload.IndexOf(entry);
        var startIndex = entryIndex - 1;
        while (startIndex >= 0 && !vm.FilesToUpload[startIndex].IsSelected)
            --startIndex;

        ++startIndex;
        for (var index = startIndex; index <= entryIndex; ++index)
            vm.FilesToUpload[index].IsSelected = true;
    }

    private static UploadWindowViewModel.UploadFileEntry? FindUploadEntry(Control? control)
    {
        while (control != null)
        {
            if (control.DataContext is UploadWindowViewModel.UploadFileEntry entry)
                return entry;

            control = control.Parent as Control;
        }

        return null;
    }
}
