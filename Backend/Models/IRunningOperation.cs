using DualView.Shared.Models;

namespace Backend.Models;

/// <summary>
///   A long-running operation triggered by the user
/// </summary>
public interface IRunningOperation
{
    public long Id { get; }

    public bool CanCancel { get; }
    public bool CanPause { get; }

    public bool Completed { get; }

    public void Cancel();
    public void Pause();
    public void Resume();

    /// <summary>
    ///   Called on application shutdown before the hard cancel signal
    /// </summary>
    public void NotifyShutdown();

    public OperationStatusUpdate GetStatusUpdate();
}
