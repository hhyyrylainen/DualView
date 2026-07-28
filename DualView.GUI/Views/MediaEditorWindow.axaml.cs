using System.ComponentModel;
using DualView.GUI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace DualView.GUI.Views;

public partial class MediaEditorWindow : Window
{
    public MediaEditorWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MediaEditorWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaEditorWindowViewModel.WantsToClose))
        {
            if (sender is MediaEditorWindowViewModel { WantsToClose: true })
            {
                Close();
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only react to left-click
        if (!e.Properties.IsLeftButtonPressed)
            return;

        if (DataContext is MediaEditorWindowViewModel { MouseCropMode: true } vm)
        {
            var point = e.GetPosition((Visual)sender!);
            vm.OnMouseCropPress(vm.MainMediaPreview.GetImageSpacePoint(point.X, point.Y));
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is MediaEditorWindowViewModel { MouseCropMode: true } vm)
        {
            var point = e.GetPosition((Visual)sender!);
            vm.UpdateMouseCrop(vm.MainMediaPreview.GetImageSpacePoint(point.X, point.Y));
            e.Handled = true;
        }
    }
}
