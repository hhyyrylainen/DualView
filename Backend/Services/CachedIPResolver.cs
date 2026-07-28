using System.Net;
using System.Net.Sockets;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class CachedIPResolver : ICachedIPResolver
{
    private readonly ILogger<CachedIPResolver> logger;
    private readonly IBackgroundJobs backgroundJobs;
    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    private readonly Dictionary<string, IPAddress> ipCache = new();

    private readonly Func<CancellationToken, Task> clearAction;

    public CachedIPResolver(ILogger<CachedIPResolver> logger, IBackgroundJobs backgroundJobs)
    {
        this.logger = logger;
        this.backgroundJobs = backgroundJobs;

        clearAction = ClearCache;

        // Clear every 20 minutes
        backgroundJobs.Schedule(clearAction, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(6));
    }

    public async Task<IPAddress> ResolveIPAsync(string hostname, CancellationToken cancellationToken = default)
    {
        await semaphoreSlim.WaitAsync(cancellationToken);

        try
        {
            if (ipCache.TryGetValue(hostname, out var cachedIP))
                return cachedIP;
        }
        finally
        {
            semaphoreSlim.Release();
        }

        IPAddress ipAddress;

        try
        {
            var result = await Dns.GetHostAddressesAsync(hostname, AddressFamily.InterNetwork, cancellationToken);

            if (result.Length == 0)
                throw new Exception("No IP addresses found after resolving hostname");

            // We just use the first address
            ipAddress = result[0];
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to resolve IP for hostname {Hostname}", hostname);
            throw;
        }

        // Cache the IP address for future use
        await semaphoreSlim.WaitAsync(cancellationToken);

        try
        {
            ipCache[hostname] = ipAddress;
        }
        finally
        {
            semaphoreSlim.Release();
        }

        return ipAddress;
    }

    public void Dispose()
    {
        semaphoreSlim.Dispose();
        backgroundJobs.CancelJob(clearAction);
    }

    private async Task ClearCache(CancellationToken cancellationToken)
    {
        if (!await semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            // Can't get lock so can't clear cache
            logger.LogWarning("Cannot get semaphore for clearing IP cache");
            return;
        }

        try
        {
            ipCache.Clear();
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }
}
