using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public class TextInputWindowViewModel : ViewModelBase
{
    // Design time constructor
    public TextInputWindowViewModel()
    {
        OnAccept = _ => Task.FromResult(true);
        ExplanationText = "This is an example of a text input window.";
    }

    [ActivatorUtilitiesConstructor]
    public TextInputWindowViewModel(Func<TextInputWindowViewModel, Task<bool>> onAccept)
    {
        OnAccept = onAccept;
    }

    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    } = "Input Text";

    public string InputPlaceholder
    {
        get;
        set => SetProperty(ref field, value);
    } = "Input...";

    public string? InputToolTip
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Input
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? ExplanationText
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Error
    {
        get;
        set => SetProperty(ref field, value);
    }

    public Func<TextInputWindowViewModel, Task<bool>> OnAccept { get; set; }
}
