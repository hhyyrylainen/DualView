using System.Collections.Generic;

namespace DualView.Shared.Requests;

public sealed class CollectionReorderRequest
{
    public List<long> MediaIds { get; set; } = new();

    public long? BeforeMediaId { get; set; }
}
