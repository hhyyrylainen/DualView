using DualView.Shared.Services;

namespace DualView.Server.ClientStubs;

/// <summary>
///   A stub that doesn't actually do anything
/// </summary>
public class StubRealTimeDataService : IRealtimeDataUpdateService
{
    // We don't care about these events as this is a stub
#pragma warning disable CS0067
    public event Action<long, int, int, string>? OnChatMessageTextAppend;
    public event Action? OnMediaFolderUpdated;
    public event Action<long>? OnMediaUpdated;
#pragma warning restore CS0067

    /// <summary>
    ///   Pretend to be always connected for prerendering.
    /// </summary>
    public bool IsConnected => true;

    public Task ReportWantedRealTimeData(string key)
    {
        return Task.CompletedTask;
    }

    public Task ReportNoLongerWantsRealTimeData(string key)
    {
        return Task.CompletedTask;
    }

    public Task StartConnectionAsync()
    {
        return Task.CompletedTask;
    }
}
