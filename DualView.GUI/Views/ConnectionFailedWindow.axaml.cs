using System;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class ConnectionFailedWindow : Window
{
    public ConnectionFailedWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext == null)
            return;

        if (DataContext is ConnectionFailedViewModel viewModel)
        {
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(viewModel.WantsToClose))
                    Close();
            };
        }
    }
}
