using System.Collections.Generic;
using DualView.GUI.ViewModels;

namespace DualView.GUI.Models;

public sealed class ImportMediaDragData
{
    public ImportMediaDragData(ImportSectionViewModel sourceSection, IReadOnlyList<long> mediaIds)
    {
        SourceSection = sourceSection;
        MediaIds = mediaIds;
    }

    public ImportSectionViewModel SourceSection { get; }
    public IReadOnlyList<long> MediaIds { get; }
}
