using System.Collections.Generic;
using System.Linq;
using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace DualView.GUI.Views;

public partial class ImportWindow : Window
{
    private static readonly IReadOnlyList<string> SupportedImagePatterns =
    [
        "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.avif", "*.mp4", "*.mkv", "*.webm", "*.mp3", "*.wav", "*.mp3",
        "*.flac",
    ];

    private static readonly IReadOnlyList<string> SupportedImageExtensions =
        SupportedImagePatterns.Select(p => p.Substring(1)).ToList();

    public ImportWindow()
    {
        InitializeComponent();

        BrowseItemsButton.Click += OpenFilePicker;

        // Enable Drag and Drop for the window
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private async void OpenFilePicker(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportWindowViewModel vm)
            return;

        // Use the modern StorageProvider API
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Media Files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media Files")
                {
                    Patterns = SupportedImagePatterns,
                },
                FilePickerFileTypes.All,
            ]
        });

        if (files.Any())
        {
            vm.AddImages(files.Select(f => f.Path.LocalPath).ToList());
        }
    }

    // TODO: this and the next method is untested as apparently DnD is not implemented on Linux for Avalonia
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only allow dropping files
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            if (DataContext is ImportWindowViewModel vm)
            {
                if (vm.DeleteAfterImport)
                {
                    e.DragEffects = DragDropEffects.Move;
                    return;
                }
            }

            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ImportWindowViewModel vm)
            return;

        var items = e.DataTransfer.Items;

        var results = new List<string>();

        foreach (var item in items)
        {
            var data = item.TryGetRaw(DataFormat.File);
            if (data is IStorageItem storageItem)
            {
                results.Add(storageItem.Path.LocalPath);
            }
        }

        if (results.Count > 0)
        {
            // Remove files that don't match the pattern
            results = results.Where(f => SupportedImageExtensions.Any(f.EndsWith)).ToList();

            vm.AddImages(results);
        }

        e.Handled = true;
    }
}
