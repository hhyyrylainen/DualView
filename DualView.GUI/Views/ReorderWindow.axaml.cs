using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class ReorderWindow : Window
{
    public ReorderWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
