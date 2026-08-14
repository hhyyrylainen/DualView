namespace DualView.Shared.Requests;

public class CollectionMediaImportRequest
{
    public List<long> MediaIds { get; set; } = new();

    public int FirstSequenceNumber { get; set; }

    public List<int>? SequenceNumbers { get; set; }
}
