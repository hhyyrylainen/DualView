using Microsoft.AspNetCore.SignalR.Client;

namespace DualView.Shared.Services;

public abstract class RealTimeUpdateServiceBase : IRealtimeDataUpdateService
{
    public event Action<long, int, int, string>? OnChatMessageTextAppend;

    private readonly HashSet<string> wantedListeners = new();

    public abstract bool IsConnected { get; }

    public async Task ReportWantedRealTimeData(string key)
    {
        wantedListeners.Add(key);

        await StartConnectionAsync();
    }

    public async Task ReportNoLongerWantsRealTimeData(string key)
    {
        wantedListeners.Remove(key);

        if (wantedListeners.Count == 0)
        {
            await StopConnectionAsync();
        }
    }

    /// <summary>
    ///   Starts connection if not connected yet.
    /// </summary>
    /// <returns>Task</returns>
    public abstract Task StartConnectionAsync();

    /// <summary>
    ///   Allows stopping the connection.
    /// </summary>
    /// <returns>Task</returns>
    public abstract Task StopConnectionAsync();

    protected void RegisterBaseListeners(HubConnection hubConnection)
    {
        hubConnection.On(nameof(IRealTimeDataHub.OnChatMessageTextAppend),
            (long chatId, int runnerId, int messageId, string text) =>
            {
                OnChatMessageTextAppend?.Invoke(chatId, runnerId, messageId, text);
            });
    }
}
