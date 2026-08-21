using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class TagEditor : UserControl
{
    public TagEditor()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, true);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is TagEditorViewModel viewModel &&
            sender is AutoCompleteBox { SelectedItem: null, Text: var text })
        {
            _ = viewModel.LoadSuggestionsAsync(text ?? string.Empty);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TagEditorViewModel viewModel)
        {
            _ = viewModel.AddTagAsync(viewModel.TagText);
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
