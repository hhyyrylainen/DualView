using System.Threading.Tasks;

namespace DualView.GUI.Models;

public interface ICollectionBrowse
{
    public Task<int> GetCountAsync();

    public Task<int?> GetIndexAsync(long mediaId);

    public Task<IVisualMediaSource?> GetMediaAsync(int index);
}
