using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ConfirmationWindowViewModel : ViewModelBase
{
    private readonly ILogger<ConfirmationWindowViewModel>? logger;

    public delegate void OnOptionSelectedHandler(bool? value);

    public event OnOptionSelectedHandler? OnOptionSelected;

    // Default constructor for design-time
    public ConfirmationWindowViewModel()
    {
    }

    [ActivatorUtilitiesConstructor]
    public ConfirmationWindowViewModel(ILogger<ConfirmationWindowViewModel> logger)
    {
        this.logger = logger;

        logger.LogInformation("Opened a confirmation window");
    }

    public string Title
    {
        get => field;
        set => SetProperty(ref field, value);
    } = "DualView - Continue?";

    public string Message
    {
        get => field;
        set => SetProperty(ref field, value);
    } = "You are about to do a potentially dangerous operation. Continue?";

    public bool WantsToClose
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    public bool ShowCancel
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    public void Cancel()
    {
        WarnIfNoCallback();

        OnOptionSelected?.Invoke(null);
        WantsToClose = true;
    }

    public void Accept()
    {
        WarnIfNoCallback();

        OnOptionSelected?.Invoke(true);
        WantsToClose = true;
    }

    public void Reject()
    {
        WarnIfNoCallback();

        OnOptionSelected?.Invoke(false);
        WantsToClose = true;
    }

    private void WarnIfNoCallback()
    {
        if (OnOptionSelected != null)
            return;

        logger?.LogWarning("No callback set for confirmation window");
    }
}
