using System.ComponentModel;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class OperationStatusWindow : Window
{
    public OperationStatusWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is OperationStatusWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is OperationStatusWindowViewModel vm)
        {
            if (!vm.WantsToClose)
            {
                // Show a warning if trying to close before it is time to do so
                vm.TryClose();

                if (!vm.WantsToClose)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        base.OnClosing(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationStatusWindowViewModel.WantsToClose))
        {
            if (sender is OperationStatusWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }
}
