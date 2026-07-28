using DualView.Shared.Models;
using Backend.Models;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Operations;

/// <summary>
///   Base class for operations that count how many items they have and then run them one by one
/// </summary>
public abstract class BaseOperationWithItemCount : IRunningOperation
{
    protected readonly ILogger Logger;
    protected readonly IEntityUpdateNotifier UpdateNotifier;

    protected readonly IServiceScope CreatedScope;

    private protected CancellationToken CancellationToken;

    protected bool RunOperation = true;
    protected bool Paused;
    protected bool RunnerPaused;

    protected int Processed;
    protected int Total;

    private readonly CancellationTokenSource mainCancellationTokenSource = new();

    public BaseOperationWithItemCount(long id, ILogger logger, IServiceScopeFactory scopeFactory)
    {
        CreatedScope = scopeFactory.CreateScope();

        Logger = logger;
        UpdateNotifier = CreatedScope.ServiceProvider.GetRequiredService<IEntityUpdateNotifier>();
        Id = id;
        CancellationToken = mainCancellationTokenSource.Token;
    }

    public long Id { get; }
    public virtual bool CanCancel => true;
    public virtual bool CanPause => true;
    public bool Completed { get; protected set; }

    public bool HasError { get; protected set; }

    public abstract string Name { get; }

    public virtual void Cancel()
    {
        mainCancellationTokenSource.Cancel();
    }

    public virtual void Pause()
    {
        Paused = true;
    }

    public virtual void Resume()
    {
        Paused = false;
    }

    public virtual void NotifyShutdown()
    {
        RunOperation = false;
    }

    public abstract OperationStatusUpdate GetStatusUpdate();

    public virtual async Task Run()
    {
        try
        {
            await UpdateNotifier.OperationStatusUpdated(GetStatusUpdate());
            Total = await CountTotalItems();
        }
        catch (OperationCanceledException)
        {
            RunOperation = false;
        }
        catch (Exception e)
        {
            HasError = true;
            Completed = true;
            await UpdateNotifier.OperationStatusUpdated(GetStatusUpdate());
            Logger.LogError(e, "Failed to count items for {Name}", Name);
            return;
        }

        try
        {
            while (RunOperation)
            {
                if (CancellationToken.IsCancellationRequested)
                    break;

                if (Paused)
                {
                    RunnerPaused = true;
                    await UpdateNotifier.OperationStatusUpdated(GetStatusUpdate());
                    await Task.Delay(1000, CancellationToken);
                    continue;
                }

                RunnerPaused = false;

                if (!await ProcessNextItem())
                {
                    break;
                }

                ++Processed;

                // Make sure the total doesn't get out of sync
                if (Processed > Total)
                    Total = Processed;

                await UpdateNotifier.OperationStatusUpdated(GetStatusUpdate());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            HasError = true;
            Logger.LogError(e, "Failed to run {Name}", Name);
        }
        finally
        {
            Completed = true;
        }

        try
        {
            await UpdateNotifier.OperationStatusUpdated(GetStatusUpdate());
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to send final operation status update");
        }
        finally
        {
            CreatedScope.Dispose();
        }
    }

    protected async Task SendCustomStatusUpdate(OperationStatusUpdate update)
    {
        try
        {
            await UpdateNotifier.OperationStatusUpdated(update);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to send custom status update");
        }
    }

    protected abstract Task<int> CountTotalItems();

    /// <summary>
    ///   Processes the next item in the list.
    /// </summary>
    /// <returns>Return false once no more items</returns>
    protected abstract Task<bool> ProcessNextItem();
}
