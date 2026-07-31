using System;
using System.Threading.Tasks;
using DualView.GUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class RenameWindowViewModel : ViewModelBase
{
    private readonly ILogger<RenameWindowViewModel>? logger;
    private readonly IWindowService? windowService;

    private Func<string, Task<(bool Valid, string Error)>>? verifier;
    private Func<string, Task<bool>>? applier;

    public RenameWindowViewModel()
    {
        // Design time
    }

    [ActivatorUtilitiesConstructor]
    public RenameWindowViewModel(ILogger<RenameWindowViewModel> logger, IWindowService windowService)
    {
        this.logger = logger;
        this.windowService = windowService;
    }

    public string NewName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _ = Validate();
            }
        }
    } = "";

    public string ErrorMessage
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public bool IsValid
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool IsApplying
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool WantsToClose
    {
        get;
        set => SetProperty(ref field, value);
    }

    public void Initialize(string originalName, Func<string, Task<(bool Valid, string Error)>> verifyFunc,
        Func<string, Task<bool>> applyFunc)
    {
        NewName = originalName;
        verifier = verifyFunc;
        applier = applyFunc;
        
        OnPropertyChanged(nameof(NewName));
    }

    public async Task Apply()
    {
        if (IsApplying || !IsValid || applier == null) return;

        IsApplying = true;

        try
        {
            if (await applier(NewName))
            {
                WantsToClose = true;
            }
            else
            {
                ErrorMessage = "Failed to apply new name";
            }
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to rename", e);
        }
        finally
        {
            IsApplying = false;
        }
    }

    private async Task Validate()
    {
        if (verifier == null) return;

        var (valid, error) = await verifier(NewName);
        IsValid = valid;
        ErrorMessage = error;
    }
}
