using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public class RealTimeDataHub : Hub<IRealTimeDataHub>
{
}
