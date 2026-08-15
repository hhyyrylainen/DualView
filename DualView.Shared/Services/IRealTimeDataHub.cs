namespace DualView.Shared.Services;

/// <summary>
///   This is for bulk data that changes a lot. In contrast to <see cref="ISignalRService"/> which is for most other
///   data that doesn't change that often. So most of the time any new signal should be added to that other interface!
/// </summary>
public interface IRealTimeDataHub
{
    public Task OnChatMessageTextAppend(long chatId, int messageIndex, int messageGeneration, string newText);

    public Task OnMediaFolderUpdated();
    public Task OnMediaUpdated(long id);
}
