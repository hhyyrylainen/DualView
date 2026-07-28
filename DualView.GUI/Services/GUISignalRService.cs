using System;
using System.Threading.Tasks;
using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public class GUISignalRService : SignalRServiceBase, IDisposable
{
    private readonly HubConnection hubConnection;

    public override event Action<bool>? OnConnectionStatusChanged;

    private bool disposedValue;

    public GUISignalRService(ILogger<GUISignalRService> logger, IGuiConfigurationService guiConfigurationService) :
        base(logger)
    {
        hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(guiConfigurationService.BackendUrl, "/hubs/runner"))
            .WithAutomaticReconnect(new RetryPolicy())
            .WithServerTimeout(TimeSpan.FromSeconds(30))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(10))
            .Build();

        // Register listeners specified in the interface
        RegisterBaseListeners(hubConnection);

        // Life cycle events

        hubConnection.Reconnecting += (error) =>
        {
            Logger.LogError(error, "SignalR error:");
            Logger.LogError("Connection lost. Reconnecting...");
            OnConnectionStatusChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        hubConnection.Reconnected += (connectionId) =>
        {
            Logger.LogInformation("Connection re-established (with ID: {ConnectionId})", connectionId);
            OnConnectionStatusChanged?.Invoke(true);

            // Optional: Force a data refresh here if needed
            return Task.CompletedTask;
        };

        hubConnection.Closed += (error) =>
        {
            if (disposedValue)
            {
                Logger.LogInformation(error, "SignalR connection closed by us");
            }
            else
            {
                Logger.LogError(error, "Connection closed completely");
                OnConnectionStatusChanged?.Invoke(false);
            }

            return Task.CompletedTask;
        };
    }

    public override bool IsConnected => hubConnection.State == HubConnectionState.Connected;

    public override async Task StartConnectionAsync()
    {
        // Prevent starting if already started
        if (hubConnection.State != HubConnectionState.Disconnected)
            return;

        try
        {
            await hubConnection.StartAsync();
            OnConnectionStatusChanged?.Invoke(true);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error starting SignalR connection");
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    public void Dispose()
    {
        disposedValue = true;
        hubConnection.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30));
    }

    private class RetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            // Try every few seconds at first
            if (retryContext.PreviousRetryCount < 5)
            {
                return TimeSpan.FromSeconds(2);
            }

            if (retryContext.PreviousRetryCount < 30)
            {
                return TimeSpan.FromSeconds(15);
            }

            // Then infinitely retry once per minute
            return TimeSpan.FromMinutes(1);
        }
    }
}
