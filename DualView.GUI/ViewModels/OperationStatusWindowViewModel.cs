using System;
using System.Threading.Tasks;
using DualView.Shared.Models;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class OperationStatusWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<OperationStatusWindowViewModel>? logger;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;

    // Design time constructor
    public OperationStatusWindowViewModel()
    {
        StatusMessage = "Example operation 4 / 22";
        SubMessage = "Processing some item or something...";
        CompletionFraction = 0.23f;
    }

    [ActivatorUtilitiesConstructor]
    public OperationStatusWindowViewModel(ILogger<OperationStatusWindowViewModel> logger,
        ISignalRService signalRService, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;

        signalRService.OnBackgroundOperationStatusUpdate += CheckStatusUpdate;
    }

    public long OperationId { get; set; }

    public string? StatusMessage
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? SubMessage
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? ErrorMessage
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool Completed
    {
        get;
        set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(NotPausedAndNotCompleted));
        }
    }

    public bool HasError
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ShowForceCloseButton
    {
        get;
        set => SetProperty(ref field, value);
    }

    public float CompletionFraction
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool Paused
    {
        get;
        set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(NotPausedAndNotCompleted));
        }
    }

    public bool NotPausedAndNotCompleted => !Paused && !Completed;

    public bool WantsToClose
    {
        get;
        set => SetProperty(ref field, value);
    }

    public void TryClose()
    {
        if (Completed)
        {
            logger?.LogInformation("Closing completed status window");
            WantsToClose = true;
        }
        else
        {
            ShowForceCloseButton = true;
            ErrorMessage = "Operation is not completed. Closing this window will not cancel the operation!";
        }
    }

    public void ForceClose()
    {
        WantsToClose = true;
    }

    public void RequestPause()
    {
        if (backendAPI == null)
            return;

        SubMessage = "Requesting pause...";

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.PauseOperation(OperationId))
                    throw new Exception("Failed to pause operation");
            }
            catch (Exception e)
            {
                logger?.LogError(e, "Failed to pause operation");
                ErrorMessage = "Failed to pause the operation";
            }
        });
    }

    public void RequestResume()
    {
        if (backendAPI == null)
            return;

        SubMessage = "Requesting resume...";

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.ResumeOperation(OperationId))
                    throw new Exception("Failed to resume operation");
            }
            catch (Exception e)
            {
                logger?.LogError(e, "Failed to resume operation");
                ErrorMessage = "Failed to resume the operation";
            }
        });
    }

    public void RequestCancel()
    {
        if (backendAPI == null)
            return;

        SubMessage = "Requesting cancellation...";
        ErrorMessage = null;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.CancelOperation(OperationId))
                    throw new Exception("Failed to cancel operation");
            }
            catch (Exception e)
            {
                logger?.LogError(e, "Failed to cancel operation");
                ErrorMessage = "Failed to cancel the operation";
            }
        });
    }

    public void Dispose()
    {
        if (signalRService != null)
        {
            signalRService.OnBackgroundOperationStatusUpdate -= CheckStatusUpdate;
        }
    }

    private void CheckStatusUpdate(OperationStatusUpdate update)
    {
        if (update.OperationId != OperationId)
        {
            // Ignore if not for us
            return;
        }

        logger?.LogDebug("Got status update for operation: {Id}", OperationId);

        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = update.MainStatusText;
            SubMessage = update.Message;

            if (update.Error)
                ErrorMessage = update.Message;

            Completed = update.Completed;
            HasError = update.Error;
            CompletionFraction = update.CompletionFraction;
            Paused = update.Paused;
        });
    }
}
