using DualView.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DualView.Client.Services;

public class SignalRService : SignalRServiceBase, IAsyncDisposable
{
    private readonly HubConnection hubConnection;

    public override event Action<bool>? OnConnectionStatusChanged;

    public SignalRService(ILogger<SignalRService> logger, NavigationManager navManager) : base(logger)
    {
        hubConnection = new HubConnectionBuilder()
            .WithUrl(navManager.ToAbsoluteUri("/hubs/runner"))
            .WithAutomaticReconnect(new RetryPolicy())
            .Build();

        // Register listeners specified in the interface
        RegisterBaseListeners(hubConnection);

        // Life cycle events

        hubConnection.Reconnecting += (error) =>
        {
            // This is just information level as we don't want to send this to the server
            this.Logger.LogInformation("Connection lost. Reconnecting...");
            OnConnectionStatusChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        hubConnection.Reconnected += (connectionId) =>
        {
            this.Logger.LogInformation("Connection re-established");
            OnConnectionStatusChanged?.Invoke(true);
            // Optional: Force a data refresh here if needed
            return Task.CompletedTask;
        };

        hubConnection.Closed += (error) =>
        {
            this.Logger.LogWarning("Connection closed completely");
            OnConnectionStatusChanged?.Invoke(false);
            return Task.CompletedTask;
        };
    }

    public override bool IsConnected => hubConnection?.State == HubConnectionState.Connected;

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
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting SignalR connection");
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection != null!)
        {
            await hubConnection.DisposeAsync();
        }
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
