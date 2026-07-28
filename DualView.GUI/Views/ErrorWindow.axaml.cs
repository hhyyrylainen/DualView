using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();

        CopyErrorButton.Click += CopyError;
    }

    private async void CopyError(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ErrorWindowViewModel errorViewModel && Clipboard != null)
        {
            await Clipboard.SetTextAsync(string.IsNullOrWhiteSpace(errorViewModel.Details)
                ? errorViewModel.ErrorMessage
                : errorViewModel.Details);
        }
    }
}
