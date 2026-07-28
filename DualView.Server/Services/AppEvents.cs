using Backend.Services;

namespace DualView.Server.Services;

/// <summary>
///   Server-side notification bus. Also sends events to connected clients.
/// </summary>
public class AppEvents : IAppEvents
{
    private readonly object @lock = new();

    private event IAppEvents.NoParamsEventHandler? SettingsChangedInternal;
    private event IAppEvents.NoParamsEventHandler? RunnerSettingsChangedInternal;
    private event IAppEvents.JobCreatedEventHandler? NewJobCreatedInternal;
    private event IAppEvents.JobCreatedEventHandler? JobCompletedInternal;
    private event IAppEvents.FlowChangedEventHandler? FlowChangedInternal;

    public event IAppEvents.NoParamsEventHandler? SettingsChanged
    {
        add
        {
            lock (@lock) SettingsChangedInternal += value;
        }
        remove
        {
            lock (@lock) SettingsChangedInternal -= value;
        }
    }

    public event IAppEvents.NoParamsEventHandler? RunnerSettingsChanged
    {
        add
        {
            lock (@lock) RunnerSettingsChangedInternal += value;
        }
        remove
        {
            lock (@lock) RunnerSettingsChangedInternal -= value;
        }
    }

    public event IAppEvents.JobCreatedEventHandler? NewJobCreated
    {
        add
        {
            lock (@lock) NewJobCreatedInternal += value;
        }
        remove
        {
            lock (@lock) NewJobCreatedInternal -= value;
        }
    }

    public event IAppEvents.JobCreatedEventHandler? JobCompleted
    {
        add
        {
            lock (@lock) JobCompletedInternal += value;
        }
        remove
        {
            lock (@lock) JobCompletedInternal -= value;
        }
    }

    public event IAppEvents.FlowChangedEventHandler? FlowChanged
    {
        add
        {
            lock (@lock) FlowChangedInternal += value;
        }
        remove
        {
            lock (@lock) FlowChangedInternal -= value;
        }
    }

    public void NotifySettingsChanged()
    {
        lock (@lock)
        {
            var handlers = SettingsChangedInternal;
            handlers?.Invoke();
        }
    }

    public void NotifyDualViewSettingsChanged()
    {
        lock (@lock)
        {
            var handlers = RunnerSettingsChangedInternal;
            handlers?.Invoke();
        }
    }

    public void NotifyNewJobCreated(long aiJobId)
    {
        lock (@lock)
        {
            var handlers = NewJobCreatedInternal;
            handlers?.Invoke(aiJobId);
        }
    }

    public void NotifyJobCompleted(long aiJobId)
    {
        lock (@lock)
        {
            var handlers = JobCompletedInternal;
            handlers?.Invoke(aiJobId);
        }
    }

    public void NotifyFlowChanged(long flowId)
    {
        lock (@lock)
        {
            var handlers = FlowChangedInternal;
            handlers?.Invoke(flowId);
        }
    }
}
