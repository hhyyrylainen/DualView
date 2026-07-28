using DualView.GUI.ViewModels;
using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class MediaPickerWindow : Window
{
    public MediaPickerWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MediaPickerWindowViewModel vm)
            {
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MediaPickerWindowViewModel.WantsToClose) && vm.WantsToClose)
                    {
                        Close(vm.Result);
                    }
                };
            }
        };
    }

}

