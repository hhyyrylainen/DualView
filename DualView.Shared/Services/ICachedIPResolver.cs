using System.Net;

namespace DualView.Shared.Services;

public interface ICachedIPResolver : IDisposable
{
    public Task<IPAddress> ResolveIPAsync(string hostname, CancellationToken cancellationToken = default);
}
