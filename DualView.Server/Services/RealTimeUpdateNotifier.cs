using DualView.Shared.Services;
using Microsoft.AspNetCore.SignalR;

namespace DualView.Server.Services;

public class RealTimeUpdateNotifier : IRealTimeDataHub
{
    private readonly IHubContext<RealTimeDataHub, IRealTimeDataHub> hubContext;

    public RealTimeUpdateNotifier(IHubContext<RealTimeDataHub, IRealTimeDataHub> hubContext)
    {
        this.hubContext = hubContext;
    }

    public async Task OnChatMessageTextAppend(long chatId, int messageIndex, int messageGeneration, string newText)
    {
        await hubContext.Clients.All.OnChatMessageTextAppend(chatId, messageIndex, messageGeneration, newText);
    }
}
