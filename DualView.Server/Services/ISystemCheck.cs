using DualView.Shared.Models;

namespace DualView.Server.Services;

public interface ISystemCheck
{
    public Task<RequiredToolCheckResult> CheckRequiredTools(CancellationToken cancellation);
}
