using System.ComponentModel;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is ConfirmationWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfirmationWindowViewModel.WantsToClose))
        {
            if (sender is ConfirmationWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }
}
