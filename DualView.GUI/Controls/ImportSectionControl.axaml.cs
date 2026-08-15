using Avalonia.Controls;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class ImportSectionControl : UserControl
{
    public ImportSectionControl()
    {
        InitializeComponent();
    }

    private void OnTargetNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { SelectedItem: null, Text: var text } &&
            DataContext is ImportSectionViewModel viewModel)
        {
            _ = viewModel.LoadTargetNameSuggestionsAsync(text ?? string.Empty);
        }
    }
}
