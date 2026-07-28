using System.ComponentModel;
using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class MediaEditSelectorWindow : Window
{
    public MediaEditSelectorWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MediaEditSelectorWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaEditSelectorWindowViewModel.WantsToClose))
        {
            if (sender is MediaEditSelectorWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }
}
