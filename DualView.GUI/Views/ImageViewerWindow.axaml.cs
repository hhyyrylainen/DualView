using System.Threading.Tasks;
using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace DualView.GUI.Views;

public partial class ImageViewerWindow : Window
{
    public ImageViewerWindow()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => Close();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is ImageViewerWindowViewModel vm)
            {
                vm.OnClipboardTextSet += text => _ = Clipboard?.SetTextAsync(text);

                vm.OnMediaSaveRequested += AskForSaveFolder;
            }
        };
    }

    private async Task<string?> AskForSaveFolder(string suggestedName)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save file",
                SuggestedFileName = suggestedName,
                ShowOverwritePrompt = true,
            });

            // Convert to a local filesystem path (null for non-local providers).
            return file?.TryGetLocalPath();
        });
    }
}
