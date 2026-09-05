using System.Threading.Tasks;

namespace DualView.GUI.Models;

public interface ICollectionBrowse
{
    public Task<(int Count, int? Index)> GetBrowseInfoAsync(long? mediaId);

    public Task<IVisualMediaSource?> GetMediaAsync(int index);
}

public interface IMediaSelectionBrowse
{
    public bool IsSelected(long mediaId);

    public void SetSelected(long mediaId, bool selected);
}
