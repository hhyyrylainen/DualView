namespace Backend.Services;

/// <summary>
///   Server-side events that communicate to other parts of the server that something changed. These are not for
///   triggering external change notifications but just for synchronizing server state with itself.
/// </summary>
public interface IAppEvents
{
    public delegate void NoParamsEventHandler();
    public delegate void JobCreatedEventHandler(long aiJobId);
    public delegate void FlowChangedEventHandler(long flowId);

    // Events
    public event NoParamsEventHandler SettingsChanged;
    public event NoParamsEventHandler RunnerSettingsChanged;
    public event JobCreatedEventHandler NewJobCreated;
    public event JobCreatedEventHandler JobCompleted;
    public event FlowChangedEventHandler FlowChanged;

    // Triggering events
    public void NotifySettingsChanged();
    public void NotifyDualViewSettingsChanged();
    public void NotifyNewJobCreated(long aiJobId);
    public void NotifyJobCompleted(long aiJobId);
    public void NotifyFlowChanged(long flowId);
}
