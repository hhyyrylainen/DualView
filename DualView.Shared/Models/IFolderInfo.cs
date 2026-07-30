namespace DualView.Shared.Models;

public interface IFolderInfo
{
    public long Id { get; }

    public string Name { get; }

    public List<long> ParentIds { get; }
}
