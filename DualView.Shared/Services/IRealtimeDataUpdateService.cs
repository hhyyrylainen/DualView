namespace DualView.Shared.Services;

/// <summary>
///   A similar service to <see cref="ISignalRService"/> but this is purely for realtime data not all clients may want
///   to listen to constantly
/// </summary>
public interface IRealtimeDataUpdateService
{
    /// <summary>
    ///   Called when a new part of text has arrived in a chat. The two ints are the index and the generation id.
    /// </summary>
    public event Action<long, int, int, string>? OnChatMessageTextAppend;

    public bool IsConnected { get; }

    /// <summary>
    ///   Report that a component wants real-time updates. This is the primary way to use this as multiple components
    ///   at once may want realtime updates.
    /// </summary>
    /// <param name="key">Unique key of the component requesting real-time updates</param>
    public Task ReportWantedRealTimeData(string key);

    /// <summary>
    ///   Stop connection if no components are listening for real-time updates any more.
    /// </summary>
    /// <param name="key">Key of the component that no longer wants real-time updates</param>
    public Task ReportNoLongerWantsRealTimeData(string key);

    public Task StartConnectionAsync();
}
