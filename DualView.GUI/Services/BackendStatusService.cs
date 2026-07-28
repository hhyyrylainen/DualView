using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public class BackendStatusService : IBackendStatusService, IDisposable
{
    private readonly ILogger<BackendStatusService> logger;
    private readonly ISignalRService signalRService;
    private readonly IBackgroundJobs backgroundJobs;
    private readonly HttpClient httpClient;

    private DateTime lastPing;

    private Func<CancellationToken, Task>? scheduledTask;

    private bool initialConnectFailed;

    public event Action<bool>? OnStatusChanged;

    public BackendStatusService(ILogger<BackendStatusService> logger, ISignalRService signalRService,
        IGuiConfigurationService guiConfigurationService, IBackgroundJobs backgroundJobs)
    {
        this.logger = logger;
        this.signalRService = signalRService;
        this.backgroundJobs = backgroundJobs;

        httpClient = new HttpClient();
        httpClient.BaseAddress = guiConfigurationService.BackendUrl;
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        lastPing = DateTime.UtcNow;

        signalRService.OnConnectionStatusChanged += SignalRStatusChanged;
    }

    public bool IsConnected
    {
        get => field;
        private set
        {
            if (field == value)
                return;

            if (value)
                initialConnectFailed = false;

            field = value;
            OnStatusChanged?.Invoke(value);
        }
    }

    public async Task StartBackendConnectionAsync()
    {
        var signalRTask = signalRService.StartConnectionAsync();

        // Ping the backend to see if it is up
        try
        {
            (await httpClient.GetAsync("api/v1/ping")).EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning("Failed to ping backend: {ExceptionMessage}", e.Message);
            OnStatusChanged?.Invoke(false);
            await signalRTask;

            initialConnectFailed = !signalRService.IsConnected;
            return;
        }

        await signalRTask;

        if (!signalRService.IsConnected)
        {
            logger.LogWarning("Giving up on connecting to backend (SignalR)");
            OnStatusChanged?.Invoke(false);
            initialConnectFailed = true;
            return;
        }

        logger.LogInformation("Backend connection established");
        IsConnected = true;
        initialConnectFailed = false;
    }

    public void BeginMonitoring()
    {
        if (scheduledTask != null)
            return;

        scheduledTask = RefreshStatus;
        backgroundJobs.Schedule(scheduledTask, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (scheduledTask != null)
        {
            backgroundJobs.CancelJob(scheduledTask);
            scheduledTask = null;
        }

        signalRService.OnConnectionStatusChanged -= SignalRStatusChanged;
    }

    private void SignalRStatusChanged(bool connected)
    {
        // If losing the connection, react immediately
        if (!connected && IsConnected)
        {
            logger.LogInformation("Received event that SignalR is no longer connected");
            IsConnected = false;
        }
    }

    private async Task RefreshStatus(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            if (!signalRService.IsConnected)
            {
                logger.LogWarning("SignalR is no longer connected");
                IsConnected = false;
                return;
            }

            if (DateTime.UtcNow - lastPing < TimeSpan.FromMinutes(1))
                return;

            lastPing = DateTime.UtcNow;

            // Ping every minute to make sure the API keeps working
            try
            {
                (await httpClient.GetAsync("api/v1/ping", cancellationToken)).EnsureSuccessStatusCode();
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("Failed to ping backend: {ExceptionMessage}", e.Message);
                IsConnected = false;
            }
        }
        else
        {
            try
            {
                (await httpClient.GetAsync("api/v1/ping", cancellationToken)).EnsureSuccessStatusCode();
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning("Failed to ping backend: {ExceptionMessage}", e.Message);
                return;
            }

            logger.LogInformation("Ping succeeded to backend");

            // If the initial connection failed, we need to restart SignalR
            if (initialConnectFailed)
            {
                logger.LogInformation("Restarting SignalR");
                await signalRService.StartConnectionAsync();
            }

            // Check if we can connect now
            if (signalRService.IsConnected)
            {
                logger.LogInformation("SignalR is now connected");

                initialConnectFailed = false;

                logger.LogInformation("Backend connection established");
                IsConnected = true;
            }
        }
    }
}
