using System;
using System.Threading.Tasks;
using DualView.Shared.Models;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class InlineOperationStatusViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger? logger;
    private readonly ISignalRService? signalRService;
    private readonly IBackendAPI? backendAPI;

    public delegate void WantsToCloseRequested();

    public event WantsToCloseRequested? OnWantsToClose;

    public InlineOperationStatusViewModel()
    {
        StatusMessage = "Example operation 4 / 22";
        SubMessage = "Processing some item or something...";
        CompletionFraction = 0.23f;
    }

    [ActivatorUtilitiesConstructor]
    public InlineOperationStatusViewModel(ILogger logger, ISignalRService signalRService, IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.signalRService = signalRService;
        this.backendAPI = backendAPI;

        signalRService.OnBackgroundOperationStatusUpdate += CheckStatusUpdate;
    }

    public long OperationId { get; private set; } = -1;

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
        set
        {
            if (SetProperty(ref field, value) && value)
            {
                OnWantsToClose?.Invoke();
            }
        }
    }

    public void Setup(long operation)
    {
        if (OperationId == operation)
            return;

        Completed = false;
        Paused = false;
        StatusMessage = "Waiting for operation to start...";
        SubMessage = null;
        ErrorMessage = null;
        WantsToClose = false;

        OperationId = operation;
        logger?.LogInformation("Setting up status watch for operation: {Id}", OperationId);

        // TODO: should we want to fetch the initial operation status here?
    }

    public void TryClose()
    {
        if (Completed)
        {
            logger?.LogInformation("Closing completed status operation");
            WantsToClose = true;
        }
        else
        {
            ErrorMessage = "Operation is not completed.";
        }
    }

    public void RequestPause()
    {
        if (backendAPI == null)
        {
            return;
        }

        SubMessage = "Requesting pause...";

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.PauseOperation(OperationId))
                {
                    throw new Exception("Failed to pause operation");
                }
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
        {
            return;
        }

        SubMessage = "Requesting resume...";

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.ResumeOperation(OperationId))
                {
                    throw new Exception("Failed to resume operation");
                }
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
        {
            return;
        }

        SubMessage = "Requesting cancellation...";
        ErrorMessage = null;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await backendAPI.CancelOperation(OperationId))
                {
                    throw new Exception("Failed to cancel operation");
                }
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

        logger?.LogTrace("Got status update for operation: {Id}", OperationId);

        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = update.MainStatusText;
            SubMessage = update.Message;

            if (update.Error)
            {
                ErrorMessage = update.Message;
            }

            Completed = update.Completed;
            HasError = update.Error;
            CompletionFraction = update.CompletionFraction;
            Paused = update.Paused;
        });
    }
}
