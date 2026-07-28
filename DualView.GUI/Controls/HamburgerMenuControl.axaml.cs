using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace DualView.GUI.Controls;

public partial class HamburgerMenuControl : UserControl
{
    private bool hamburgerClickWired;

    public HamburgerMenuControl()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (!hamburgerClickWired && DataContext is HamburgerMenuViewModel)
            {
                // Wire up button click to open the popup when proper VM is set
                this.FindControl<Button>("HamburgerButton")!.Click += OnHamburgerClick;
                hamburgerClickWired = true;
            }
        };
    }

    private void OnHamburgerClick(object? sender, RoutedEventArgs e)
    {
        var popup = this.FindControl<Popup>("MenuPopup");
        if (popup is null)
            return;
        popup.IsOpen = !popup.IsOpen;
    }
}
