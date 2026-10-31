using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DualView.Server.Services;
using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualView.Server.Tests;

public class ClientViewActivationServiceTests
{
    [Fact]
    public async Task AcknowledgementStopsRetriesAndStaleAcknowledgementsAreIgnored()
    {
        var fixture = new Fixture();
        fixture.Service.ShowImporter();
        await fixture.Tick();
        var firstRequest = Assert.Single(fixture.Requests);
        fixture.Service.ImporterShown(firstRequest);
        fixture.Clock.Advance(20);
        await fixture.Tick();
        Assert.Single(fixture.Requests);
        Assert.Equal(0, fixture.Launcher.Attempts);

        fixture.Service.ShowImporter();
        fixture.Service.ImporterShown(firstRequest);
        await fixture.Tick();
        Assert.Equal(2, fixture.Requests.Count);
        Assert.NotEqual(firstRequest, fixture.Requests[1]);
    }

    [Fact]
    public async Task RequestsCoalesceAndLaunchAttemptsAreLimitedAcrossRequests()
    {
        var fixture = new Fixture();
        fixture.Service.ShowImporter();
        await fixture.Tick();
        Assert.Equal(0, fixture.Launcher.Attempts);
        fixture.Clock.Advance(3);
        fixture.Service.ShowImporter();
        await fixture.Tick();
        Assert.Equal(1, fixture.Launcher.Attempts);
        Assert.Equal(fixture.Requests[0], fixture.Requests[1]);

        fixture.Service.ImporterShown(fixture.Requests[0]);
        fixture.Service.ShowImporter();
        fixture.Clock.Advance(59);
        await fixture.Tick();
        Assert.Equal(1, fixture.Launcher.Attempts);
        fixture.Clock.Advance(1);
        await fixture.Tick();
        Assert.Equal(2, fixture.Launcher.Attempts);
    }

    [Fact]
    public async Task RecentConnectionsGetFifteenSecondsToRespond()
    {
        var fixture = new Fixture();
        fixture.Service.ShowImporter();
        fixture.Clock.Advance(3);
        fixture.Service.ClientConnected();
        fixture.Clock.Advance(14);
        await fixture.Tick();
        Assert.Equal(0, fixture.Launcher.Attempts);
        fixture.Clock.Advance(1);
        await fixture.Tick();
        Assert.Equal(1, fixture.Launcher.Attempts);
    }

    [Fact]
    public async Task MissingLauncherStillNotifiesClients()
    {
        var fixture = new Fixture(null);
        fixture.Service.ShowImporter();
        fixture.Clock.Advance(30);
        await fixture.Tick();
        Assert.Single(fixture.Requests);
        Assert.Equal(0, fixture.Launcher.Attempts);
    }

    [Fact]
    public async Task FailedLaunchIsRateLimitedAndDoesNotStopRetries()
    {
        var fixture = new Fixture();
        fixture.Launcher.Fail = true;
        fixture.Service.ShowImporter();
        fixture.Clock.Advance(3);
        await fixture.Tick();
        fixture.Clock.Advance(3);
        await fixture.Tick();
        Assert.Equal(1, fixture.Launcher.Attempts);
        Assert.Equal(2, fixture.Requests.Count);
        fixture.Clock.Advance(57);
        await fixture.Tick();
        Assert.Equal(2, fixture.Launcher.Attempts);
    }

    private sealed class Fixture
    {
        [SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
        public Fixture(string? launcherPath = "/test/gui")
        {
            var client = DispatchProxy.Create<IDataHub, HubClientProxy>();
            ((HubClientProxy)client).Requests = Requests;
            var clients = DispatchProxy.Create<IHubClients<IDataHub>, AllClientsProxy>();
            ((AllClientsProxy)clients).Client = client;
            var hub = DispatchProxy.Create<IHubContext<DataHub, IDataHub>, HubContextProxy>();
            ((HubContextProxy)hub).Clients = clients;
            Service = new ClientViewActivationService(hub, new Configuration(launcherPath), Launcher, Clock,
                NullLogger<ClientViewActivationService>.Instance);
        }

        public List<Guid> Requests { get; } = new();
        public Clock Clock { get; } = new();
        public Launcher Launcher { get; } = new();
        public ClientViewActivationService Service { get; }

        public Task Tick() => Service.ProcessPendingActivationAsync(CancellationToken.None);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }

    private sealed class Launcher : IGUILauncher
    {
        public int Attempts { get; private set; }
        public bool Fail { get; set; }

        public void Launch(string path)
        {
            ++Attempts;
            if (Fail)
                throw new IOException("Test launch failure");
        }
    }

    private sealed class Configuration(string? path) : IServerConfigurationService
    {
        public string? GUILauncher => path;
        public string? ListenUrl => null;
        public string? DatabaseFilePath => null;
        public string? LegacyDatabaseFilePath => null;
        public string? LegacyCollectionRootPath => null;
    }

    public class HubClientProxy : DispatchProxy
    {
        public List<Guid> Requests { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Requests.Add((Guid)args![0]!);
            return Task.CompletedTask;
        }
    }

    public class AllClientsProxy : DispatchProxy
    {
        public IDataHub Client { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Client;
    }

    public class HubContextProxy : DispatchProxy
    {
        public IHubClients<IDataHub> Clients { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Clients;
    }
}
