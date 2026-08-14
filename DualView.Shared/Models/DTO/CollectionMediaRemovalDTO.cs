namespace DualView.Shared.Models.DTO;

public sealed class CollectionMediaRemovalItem
{
    public long MediaId { get; set; }
    public int SequenceNumber { get; set; }
}

public sealed class CollectionMediaRemovalPreview
{
    public List<long> OrphanedMediaIds { get; set; } = new();
}

public sealed class CollectionMediaRemovalResult
{
    public long CollectionId { get; set; }
    public List<CollectionMediaRemovalItem> RemovedItems { get; set; } = new();
    public List<long> AddedToUncategorizedMediaIds { get; set; } = new();
}
