using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Layout;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Controls;

public partial class ImportSectionControl : UserControl
{
    private bool expandContent;
    private ImportSectionViewModel? dataContextViewModel;

    public ImportSectionControl()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => ApplyExpandedLayout();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    ///   When set, this takes up all the space it wants. So this is used when popped out to do a custom layout
    ///  that is completely tall.
    /// </summary>
    public bool ExpandContent
    {
        get => expandContent;
        set
        {
            if (expandContent == value)
                return;

            expandContent = value;
            var verticalAlignment = value
                ? VerticalAlignment.Stretch
                : VerticalAlignment.Top;
            CollectionLayout.VerticalAlignment = verticalAlignment;
            ImagesLayout.VerticalAlignment = verticalAlignment;
            ApplyImageSizing();

            if (!value)
                return;

            ApplyExpandedLayout();
        }
    }

    private void ApplyExpandedLayout()
    {
        if (!expandContent)
            return;

        CollectionLayout.VerticalAlignment = VerticalAlignment.Stretch;
        ImagesLayout.VerticalAlignment = VerticalAlignment.Stretch;
        ImageContentLayout.VerticalAlignment = VerticalAlignment.Stretch;
        CollectionLayout.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        FolderPickerControl.ClearValue(HeightProperty);
        FolderPickerControl.MinHeight = 305;
        ApplyImageSizing();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (dataContextViewModel != null)
            dataContextViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        dataContextViewModel = DataContext as ImportSectionViewModel;
        if (dataContextViewModel != null)
            dataContextViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyImageSizing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportSectionViewModel.PreviewScrollViewerHeight))
            ApplyImageSizing();
    }

    private void ApplyImageSizing()
    {
        ImagesScrollViewer.Height = expandContent || dataContextViewModel == null
            ? double.NaN
            : dataContextViewModel.PreviewScrollViewerHeight;
        ImagesScrollViewer.MinHeight = 0;
        ImagesScrollViewer.VerticalAlignment = VerticalAlignment.Stretch;
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
