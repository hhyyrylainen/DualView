using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public class DataHub(IClientViewActivationService viewActivation) : Hub<IDataHub>
{
    public override async Task OnConnectedAsync()
    {
        viewActivation.ClientConnected();
        await base.OnConnectedAsync();
    }

    public void ImporterShown(Guid requestId)
    {
        viewActivation.ImporterShown(requestId);
    }
}
