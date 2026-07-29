using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class AddToFolderWindow : Window
{
    public AddToFolderWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
