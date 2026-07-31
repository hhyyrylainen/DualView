using System.ComponentModel;
using Avalonia.Controls;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Views;

public partial class RenameWindow : Window
{
    public RenameWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is RenameWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RenameWindowViewModel.WantsToClose))
        {
            if (sender is RenameWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }
}
