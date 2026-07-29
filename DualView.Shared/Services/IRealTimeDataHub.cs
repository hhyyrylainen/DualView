namespace DualView.Shared.Services;

public interface IRealTimeDataHub
{
    public Task OnChatMessageTextAppend(long chatId, int messageIndex, int messageGeneration, string newText);

    public Task OnMediaFolderUpdated();
    public Task OnMediaUpdated(long id);
}
