using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public sealed class ClientViewActivationService : BackgroundService, IClientViewActivationService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectionGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromMinutes(1);

    private readonly IHubContext<DataHub, IDataHub> hub;
    private readonly IServerConfigurationService configuration;
    private readonly IGUILauncher launcher;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ClientViewActivationService> logger;
    private readonly Lock stateLock = new();

    private Guid? pendingRequest;
    private DateTimeOffset requestedAt;
    private DateTimeOffset? lastConnection;
    private DateTimeOffset? lastLaunchAttempt;

    public ClientViewActivationService(IHubContext<DataHub, IDataHub> hub,
        IServerConfigurationService configuration, IGUILauncher launcher, TimeProvider timeProvider,
        ILogger<ClientViewActivationService> logger)
    {
        this.hub = hub;
        this.configuration = configuration;
        this.launcher = launcher;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public void ShowImporter()
    {
        lock (stateLock)
        {
            if (pendingRequest.HasValue)
                return;

            pendingRequest = Guid.NewGuid();
            requestedAt = timeProvider.GetUtcNow();
        }
    }

    public void ClientConnected()
    {
        lock (stateLock)
        {
            lastConnection = timeProvider.GetUtcNow();
        }
    }

    public void ImporterShown(Guid requestId)
    {
        lock (stateLock)
        {
            if (pendingRequest == requestId)
                pendingRequest = null;
        }
    }

    internal async Task ProcessPendingActivationAsync(CancellationToken cancellation)
    {
        Guid requestId;
        lock (stateLock)
        {
            if (!pendingRequest.HasValue)
                return;

            requestId = pendingRequest.Value;
        }

        try
        {
            await hub.Clients.All.ShowImporter(requestId).WaitAsync(cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to send importer activation request");
        }

        lock (stateLock)
        {
            // Determine if it is time to attempt a GUI launch
            var now = timeProvider.GetUtcNow();
            if (pendingRequest != requestId || now - requestedAt < RetryInterval ||
                (lastConnection.HasValue && now - lastConnection.Value < ConnectionGracePeriod) ||
                (lastLaunchAttempt.HasValue && now - lastLaunchAttempt.Value < LaunchCooldown) ||
                string.IsNullOrWhiteSpace(configuration.GUILauncher))
            {
                return;
            }

            // Failed starts are also rate limited. Serialize with acknowledgements and new requests.
            lastLaunchAttempt = now;
            try
            {
                launcher.Launch(configuration.GUILauncher);
                logger.LogInformation("Started GUI to show the importer");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to launch GUI at {Path}", configuration.GUILauncher);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RetryInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProcessPendingActivationAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
