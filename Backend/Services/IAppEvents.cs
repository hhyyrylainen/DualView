using Backend.Models;

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
    public delegate void TagCreatedEventHandler(string tagName, long tagId);
    public delegate void TagUpdatedEventHandler(long tagId);
    public delegate void ScannedCollectionEventHandler(long collectionId);
    public delegate void BrowserPageScanRequestedEventHandler(BrowserPageScanRequest request);
    public delegate void BrowserPageScanResponseEventHandler(BrowserPageScanResponse response);

    // Events
    public event NoParamsEventHandler SettingsChanged;
    public event NoParamsEventHandler RunnerSettingsChanged;
    public event JobCreatedEventHandler NewJobCreated;
    public event JobCreatedEventHandler JobCompleted;
    public event FlowChangedEventHandler FlowChanged;
    public event TagCreatedEventHandler TagCreated;
    public event TagUpdatedEventHandler TagUpdated;
    public event ScannedCollectionEventHandler ScannedCollectionCreated;
    public event BrowserPageScanRequestedEventHandler BrowserPageScanRequested;
    public event BrowserPageScanResponseEventHandler BrowserPageScanResponseReceived;

    // Triggering events
    public void NotifySettingsChanged();
    public void NotifyDualViewSettingsChanged();
    public void NotifyNewJobCreated(long aiJobId);
    public void NotifyJobCompleted(long aiJobId);
    public void NotifyFlowChanged(long flowId);
    public void NotifyTagCreated(string tagName, long tagId);
    public void NotifyTagUpdated(long tagId);
    public void NotifyScannedCollectionCreated(long collectionId);
    public void NotifyBrowserPageScanRequested(BrowserPageScanRequest request);
    public void NotifyBrowserPageScanResponseReceived(BrowserPageScanResponse response);
}
