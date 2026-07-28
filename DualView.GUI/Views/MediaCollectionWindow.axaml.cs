using System;
using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace DualView.GUI.Views;

public partial class MediaCollectionWindow : Window
{
    public MediaCollectionWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MediaCollectionWindowViewModel vm)
            {
                vm.OnWantsToCreateFolder += OnNewFolder;

                // Hook clipboard copy callback for folder path copying
                vm.RequestCopyToClipboard = async text =>
                {
                    if (Clipboard == null)
                        throw new InvalidOperationException("Clipboard is not available");

                    await Clipboard.SetTextAsync(text);
                };
            }
        };
    }

    private async void OnNewFolder(object? sender, EventArgs e)
    {
        if (DataContext is not MediaCollectionWindowViewModel vm)
            return;

        var parentFolder = vm.LastSelectedFolderId;

        var folderText = parentFolder == null
            ? "It will be created in the root folder! "
            : "It will be created in the last selected folder";

        var dialogVm = new TextInputWindowViewModel(v => vm.TryCreateNewFolderAsync(v.Input, parentFolder, v))
        {
            Title = "New Media Folder",
            ExplanationText = $"Enter a name for the new folder. {folderText}:",
            InputPlaceholder = "Folder name...",
        };

        var dialog = new TextInputWindow
        {
            DataContext = dialogVm,
            Title = dialogVm.Title,
        };

        await dialog.ShowDialog<bool>(this);
    }
}
