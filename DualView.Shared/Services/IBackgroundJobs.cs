namespace DualView.Shared.Services;

/// <summary>
///   Handles periodic background jobs.
/// </summary>
public interface IBackgroundJobs
{
    public void Start();

    /// <summary>
    ///   Called when the application is shutting down.
    /// </summary>
    public void Stop(bool wait, TimeSpan timeout);

    /// <summary>
    ///   Schedules a new job to run with an interval
    /// </summary>
    /// <param name="action">Function to run</param>
    /// <param name="interval">How often to run</param>
    /// <param name="firstRunDelay">If specified, then delays the first run by this amount</param>
    public void Schedule(Func<CancellationToken, Task> action, TimeSpan interval, TimeSpan? firstRunDelay = null);

    /// <summary>
    ///   Cancels a scheduled job to stop it repeating. If the job is currently running, this will wait for it to stop.
    /// </summary>
    /// <param name="action">What to cancel; must match the exact instance</param>
    /// <returns>True if cancelled, false if the action was unknown</returns>
    public bool CancelJob(Func<CancellationToken, Task> action);
}
