using System.Collections.Generic;

namespace DualView.GUI.Models;

public sealed class CollectionMediaDragData
{
    public CollectionMediaDragData(IReadOnlyList<long> mediaIds)
    {
        MediaIds = mediaIds;
    }

    public IReadOnlyList<long> MediaIds { get; }
}
