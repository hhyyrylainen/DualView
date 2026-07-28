namespace DualView.Shared.Requests;

public class FolderMoveRequest(long folderId, bool isMove)
{
    public long FolderId { get; set; } = folderId;

    public bool IsMove { get; set; } = isMove;
}
