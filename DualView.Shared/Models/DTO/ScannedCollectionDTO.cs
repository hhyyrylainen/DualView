using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public sealed class ScannedCollectionDTO
{
    public long Id { get; init; }
    public string GalleryUrl { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool ScanFailed { get; init; }
    public bool ScanPaused { get; init; }
    public string? ScanError { get; init; }
    public string? CurrentlyScannedUrl { get; init; }
    public int FoundMediaCount { get; init; }
    public int SelectedMediaCount { get; init; }
    public List<ScannedMediaDTO> Media { get; init; } = new();
}

public sealed class ScannedCollectionStatusDTO
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool ScanFailed { get; init; }
    public bool ScanPaused { get; init; }
    public string? ScanError { get; init; }
    public string? CurrentlyScannedUrl { get; init; }
    public int FoundMediaCount { get; init; }
    public int SelectedMediaCount { get; init; }
}

public sealed class ScannedMediaDTO
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public bool Selected { get; init; }
    public string? Referrer { get; init; }
    public MediaType? MediaType { get; init; }
    public List<string> Tags { get; init; } = new();
}

public sealed class AcceptScannedCollectionRequest
{
    public string Name { get; set; } = string.Empty;
    public List<long>? MediaIds { get; set; }
}
