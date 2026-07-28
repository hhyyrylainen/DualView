using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class EditMediaFoldersWindow : Window
{
    public EditMediaFoldersWindow()
    {
        InitializeComponent();

        CancelButton.Click += (_, _) => Close();
        ApplyButton.Click += OnApply;
    }

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditMediaFoldersWindowViewModel vm)
        {
            if (!await vm.Apply())
                return;
        }

        Close();
    }
}
