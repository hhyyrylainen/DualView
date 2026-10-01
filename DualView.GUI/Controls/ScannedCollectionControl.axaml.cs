using Avalonia.Controls;
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
}
