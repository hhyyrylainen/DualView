using System;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class TextEditWindow : Window
{
    public TextEditWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is TextEditWindowViewModel vm)
            {
                vm.OnWantsToClose += OnWantsToClose;
            }
        };
    }

    private void OnWantsToClose(object? sender, EventArgs e)
    {
        Close();
    }
}
