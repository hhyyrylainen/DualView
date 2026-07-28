using System;
using DualView.GUI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DualView.GUI.Views;

public partial class TextInputWindow : Window
{
    private bool runningCallback = false;

    public TextInputWindow()
    {
        InitializeComponent();

        CancelButton.Click += (_, _) => Close(null);
        OkButton.Click += OnAccepted;
    }

    private async void OnAccepted(object? sender, RoutedEventArgs args)
    {
        if (runningCallback)
            return;

        if (DataContext is not TextInputWindowViewModel vm)
        {
            Close(false);
            return;
        }

        runningCallback = true;

        bool result;
        try
        {
            result = await vm.OnAccept.Invoke(vm);
        }
        catch (Exception e)
        {
            vm.Error = e.Message;
            return;
        }
        finally
        {
            runningCallback = false;
        }

        if (result)
        {
            Close(true);
        }

        // Stay open so that we can show any errors
    }
}
