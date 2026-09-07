using System.Collections.Generic;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Requests;

public sealed class CollectionReorderRequest
{
    public List<long> MediaIds { get; set; } = new();

    public CollectionSortColumn SortColumn { get; set; } = CollectionSortColumn.CollectionOrder;

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
}
