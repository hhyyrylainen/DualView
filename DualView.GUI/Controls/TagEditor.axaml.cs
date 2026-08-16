using Avalonia.Controls;
using Avalonia.Input;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class TagEditor : UserControl
{
    public TagEditor() => InitializeComponent();

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is TagEditorViewModel viewModel && sender is AutoCompleteBox box)
            _ = viewModel.LoadSuggestionsAsync(box.Text ?? string.Empty);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TagEditorViewModel viewModel)
        {
            viewModel.AddTag();
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is TagEditorViewModel viewModel)
        {
            viewModel.DeleteSelected();
            e.Handled = true;
        }
    }
}
