using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class FolderPicker : UserControl
{
    public FolderPicker()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not FolderPickerViewModel viewModel)
            return;

        viewModel.RequestCopyToClipboard = async text =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                throw new InvalidOperationException("Clipboard is not available");

            await clipboard.SetTextAsync(text);
        };

        viewModel.RequestPasteFromClipboard = async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
                throw new InvalidOperationException("Clipboard is not available");

            return await clipboard.TryGetTextAsync() ?? string.Empty;
        };
    }
}
