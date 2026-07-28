using System.Diagnostics;
using Backend.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public interface IOperationsStorage
{
    public IRunningOperation? GetOperation(long id);

    public long GetNextOperationId();

    public void RegisterOperation(IRunningOperation operation);

    public void OnAppShutdown();
}

public class OperationsStorage : IOperationsStorage
{
    private readonly ILogger<OperationsStorage> logger;
    private static long nextId;

    private readonly Dictionary<long, IRunningOperation> operations = new();

    private int expensiveCounter;

    public OperationsStorage(ILogger<OperationsStorage> logger)
    {
        this.logger = logger;
    }

    public IRunningOperation? GetOperation(long id)
    {
        lock (operations)
        {
            ++expensiveCounter;
            if (expensiveCounter > 100)
                PruneCompletedOperations();

            return operations.GetValueOrDefault(id);
        }
    }

    public long GetNextOperationId()
    {
        return Interlocked.Increment(ref nextId);
    }

    public void RegisterOperation(IRunningOperation operation)
    {
        if (GetOperation(operation.Id) != null)
            throw new InvalidOperationException("Operation already registered");

        lock (operations)
        {
            ++expensiveCounter;
            if (expensiveCounter > 10)
                PruneCompletedOperations();

            operations[operation.Id] = operation;
        }
    }

    public void OnAppShutdown()
    {
        lock (operations)
        {
            if (operations.Count < 1)
                return;

            foreach (var operation in operations)
            {
                try
                {
                    operation.Value.NotifyShutdown();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to notify operation {OperationId} of shutdown", operation.Key);
                }
            }
        }

        logger.LogInformation("There are long-running operations on shutdown, waiting for them to complete");

        var stopWatch = new Stopwatch();
        stopWatch.Start();

        bool canceled = false;

        while (true)
        {
            if (!canceled && stopWatch.Elapsed > TimeSpan.FromSeconds(10))
            {
                logger.LogWarning("Canceling operations as there are still some in-progress");

                lock (operations)
                {
                    foreach (var tuple in operations)
                    {
                        try
                        {
                            tuple.Value.Cancel();
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "Failed to cancel operation {OperationId}", tuple.Key);
                        }
                    }
                }

                canceled = true;
            }

            if (stopWatch.Elapsed > TimeSpan.FromMinutes(1))
            {
                logger.LogError("Could not wait for operations to complete, quitting anyway!");
                lock (operations)
                {
                    operations.Clear();
                }

                return;
            }

            lock (operations)
            {
                PruneCompletedOperations();
                if (operations.Count == 0)
                    break;
            }
        }

        logger.LogInformation("Successfully waited for operations to complete or cancel");
    }

    private void PruneCompletedOperations()
    {
        expensiveCounter = 0;

        foreach (var operation in operations.Values.ToList())
        {
            if (operation.Completed)
                operations.Remove(operation.Id);
        }
    }
}
