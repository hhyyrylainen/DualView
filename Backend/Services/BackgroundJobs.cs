using DualView.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class BackgroundJobs : IBackgroundJobs
{
    private readonly ILogger<BackgroundJobs> logger;
    private readonly List<TaskToRun> tasksToRun = new();
    private bool isRunning;
    private bool run;
    private CancellationTokenSource cancellationTokenSource = new();

    private Task? runTask;

    public BackgroundJobs(ILogger<BackgroundJobs> logger)
    {
        this.logger = logger;
    }

    public void Start()
    {
        if (run)
            return;

        run = true;
        isRunning = true;
        cancellationTokenSource = new CancellationTokenSource();
        runTask = Task.Run(RunOperationsThread);

        logger.LogDebug("Backgrounds jobs started");
    }

    public void Stop(bool wait, TimeSpan timeout)
    {
        if (!run)
            return;

        run = false;

        if (wait)
        {
            if (runTask == null)
            {
                logger.LogWarning("Using worse background cancellation for jobs");
                var start = DateTime.Now;
                cancellationTokenSource.Cancel();
                while (isRunning && DateTime.Now - start < timeout)
                {
                    Thread.Sleep(10);
                }

                if (isRunning)
                    logger.LogError("Failed to wait for background jobs to stop");
            }
            else
            {
                if (!runTask.Wait(timeout))
                {
                    logger.LogWarning("Background jobs are taking too long to stop, cancelling them");
                    cancellationTokenSource.Cancel();

                    if (!runTask.Wait(timeout))
                        logger.LogError("Failed to wait for background jobs to stop");
                }
            }
        }

        if (!isRunning)
        {
            logger.LogDebug("Backgrounds jobs stopped");
        }
        else
        {
            logger.LogWarning("Backgrounds jobs have not stopped");
        }
    }

    public void Schedule(Func<CancellationToken, Task> action, TimeSpan interval, TimeSpan? firstRunDelay = null)
    {
        lock (tasksToRun)
        {
            logger.LogDebug("Registering new task to run with interval {Interval}", interval);
            tasksToRun.Add(new TaskToRun(action, interval, firstRunDelay));
        }
    }

    public bool CancelJob(Func<CancellationToken, Task> action)
    {
        var timeOut = DateTime.Now + TimeSpan.FromSeconds(15);

        TaskToRun? taskForAction = null;

        // Find a match for the action and cancel it
        lock (tasksToRun)
        {
            foreach (var task in tasksToRun)
            {
                if (task.ThingToRun == action)
                {
                    logger.LogDebug("Canceled background task repeat");
                    if (!tasksToRun.Remove(task))
                        logger.LogError("Failed to remove task from list of tasks to run");

                    taskForAction = task;
                    break;
                }
            }
        }

        if (taskForAction != null)
        {
            while (taskForAction.IsRunning && DateTime.Now < timeOut)
            {
                Thread.Sleep(10);
            }

            if (taskForAction.IsRunning)
                logger.LogWarning("Failed to wait for task to end before removing its scheduling");

            return true;
        }

        return false;
    }

    private async Task RunOperationsThread()
    {
        var cancellationToken = cancellationTokenSource.Token;

        while (run)
        {
            bool didSomething = false;

            while (true)
            {
                TaskToRun? taskToRun = null;

                lock (tasksToRun)
                {
                    foreach (var potentialTask in tasksToRun)
                    {
                        // TODO: detecting if some job is eating so much time that others cannot run?
                        if (potentialTask.RunAfter < DateTime.Now)
                        {
                            taskToRun = potentialTask;
                            break;
                        }
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                if (taskToRun == null)
                    break;

                didSomething = true;

                taskToRun.IsRunning = true;
                try
                {
                    await taskToRun.ThingToRun(cancellationToken);
                }
                catch (Exception e)
                {
                    // TODO: show failure in the GUI
                    logger.LogError(e, "Failed to run background task");
                }
                finally
                {
                    taskToRun.IsRunning = false;
                }

                taskToRun.RunAfter = DateTime.Now + taskToRun.Interval;
            }

            if (didSomething)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                continue;
            }

            // Sleeps while nothing to do
            await Task.Delay(50, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        isRunning = false;
    }

    private class TaskToRun
    {
        public Func<CancellationToken, Task> ThingToRun;
        public TimeSpan Interval;
        public DateTime RunAfter;
        public bool IsRunning;

        public TaskToRun(Func<CancellationToken, Task> thingToRun, TimeSpan interval, TimeSpan? firstRunDelay = null)
        {
            ThingToRun = thingToRun;
            Interval = interval;
            RunAfter = DateTime.Now + (firstRunDelay ?? TimeSpan.Zero);
        }
    }
}
