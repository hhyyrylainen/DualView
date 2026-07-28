using System.ComponentModel;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class FolderSelectorWindow : Window
{
    public FolderSelectorWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is FolderSelectorWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderSelectorWindowViewModel.WantsToClose))
        {
            if (sender is FolderSelectorWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }
}
