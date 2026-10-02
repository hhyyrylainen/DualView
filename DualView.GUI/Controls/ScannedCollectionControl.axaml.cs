using Avalonia.Controls;
using Avalonia.Interactivity;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class ScannedCollectionControl : UserControl
{
    public ScannedCollectionControl()
    {
        InitializeComponent();
    }

    private void OnTargetNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: null, Text: var text } &&
            DataContext is ScannedCollectionViewModel viewModel)
        {
            _ = viewModel.LoadTargetNameSuggestionsAsync(text ?? string.Empty);
        }
    }

    private async void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScannedCollectionViewModel viewModel)
            return;

        // Make sure this is definitely up to date before accepting
        viewModel.TargetName = TargetNameInput.Text ?? string.Empty;
        await viewModel.AcceptAsync();
    }
}
