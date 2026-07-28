using DualView.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DualView.Client.Services;

public class RealTimeUpdateService : RealTimeUpdateServiceBase, IAsyncDisposable
{
    private readonly ILogger<RealTimeUpdateService> logger;
    private readonly NavigationManager navManager;
    private HubConnection? hubConnection;

    private bool closed;

    public RealTimeUpdateService(ILogger<RealTimeUpdateService> logger, NavigationManager navManager)
    {
        this.logger = logger;
        this.navManager = navManager;
    }

    public override bool IsConnected => hubConnection?.State == HubConnectionState.Connected;

    public override async Task StartConnectionAsync()
    {
        // Prevent starting if already started
        if (hubConnection != null && hubConnection.State == HubConnectionState.Connected)
            return;

        if (hubConnection == null)
        {
            hubConnection = new HubConnectionBuilder()
                .WithUrl(navManager.ToAbsoluteUri("/hubs/realtime"))
                .WithAutomaticReconnect(new RetryPolicy())
                .Build();

            // Register listeners specified in the interface
            RegisterBaseListeners(hubConnection);

            // Life cycle events

            hubConnection.Reconnecting += (_) =>
            {
                logger.LogInformation("Realtime connection lost. Reconnecting...");
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += (connectionId) =>
            {
                logger.LogInformation("Realtime connection re-established");
                return Task.CompletedTask;
            };

            hubConnection.Closed += (_) =>
            {
                logger.LogInformation("Realtime connection closed");
                closed = true;
                return Task.CompletedTask;
            };
        }

        try
        {
            await hubConnection.StartAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error starting real-time connection");
        }
    }

    public override async Task StopConnectionAsync()
    {
        if (hubConnection == null)
            return;

        if (hubConnection.State == HubConnectionState.Disconnected && closed)
            return;

        await hubConnection.StopAsync();
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
            // Retry initially very often
            if (retryContext.PreviousRetryCount < 15)
            {
                return TimeSpan.FromSeconds(1);
            }

            if (retryContext.PreviousRetryCount < 60)
            {
                return TimeSpan.FromSeconds(10);
            }

            // Then infinitely retry once per minute
            return TimeSpan.FromMinutes(1);
        }
    }
}
