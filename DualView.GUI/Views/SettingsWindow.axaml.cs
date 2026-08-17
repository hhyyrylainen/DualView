using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace DualView.GUI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        AudioBufferingSpinner.Spin += OnAudioBufferingSpin;

        MediaInputPick.Click += OnPickMediaInputFolder;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAudioBufferingSpin(object? sender, SpinEventArgs args)
    {
        var context = (SettingsWindowViewModel?)DataContext;
        if (context == null)
            return;

        if (args.Direction == SpinDirection.Decrease)
        {
            if (context.AudioBufferingMs >= 5)
            {
                context.AudioBufferingMs -= 5;
            }
            else
            {
                context.AudioBufferingMs = 0;
            }
        }
        else if (args.Direction == SpinDirection.Increase)
        {
            context.AudioBufferingMs += 5;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.RequestCopyToClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
        }
    }

    private async void OnPickMediaInputFolder(object? sender, RoutedEventArgs e)
    {
        var context = (SettingsWindowViewModel?)DataContext;
        if (context == null)
            return;

        if (!StorageProvider.CanPickFolder)
        {
            return;
        }

        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select media storage folder",
        });

        if (result.Count > 0)
        {
            context.LocalMediaStorageLocation = result[0].TryGetLocalPath() ?? result[0].Path.ToString();
        }
    }
}
