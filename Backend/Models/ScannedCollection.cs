using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   A remotely scanned collection: owns scanner state, queued pages, and discovered media.
///   Unlike <see cref="DownloadGallery"/>, this is the working collection shown to the user.
/// </summary>
[Index(nameof(Name))]
public class ScannedCollection : UpdateableModel, IDTOProvider<ScannedCollectionDTO>
{
    public ScannedCollection(string name)
    {
        Name = name;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(4096)]
    public string GalleryUrl { get; set; } = string.Empty;

    public bool ScanFailed { get; set; }

    public bool ScanPaused { get; set; }

    [MaxLength(2000)]
    public string? ScanError { get; set; }

    [MaxLength(4096)]
    public string? CurrentlyScannedUrl { get; set; }

    [MaxLength(200)]
    public string Name { get; set; }

    public long TargetFolderId { get; set; } = MediaFolder.RootFolderId;

    [MaxLength(4096)]
    public string UnrecognizedTags { get; set; } = string.Empty;

    [MaxLength(1000000)]
    public string ScannerState { get; set; } = string.Empty;

    public ICollection<AppliedTag> AppliedTags { get; set; } = new List<AppliedTag>();

    public ICollection<FoundMedia> FoundMedia { get; set; } = new List<FoundMedia>();

    public ScannedCollectionDTO GetDTO()
    {
        return new ScannedCollectionDTO
        {
            Id = Id,
            GalleryUrl = GalleryUrl,
            Name = Name,
            ScanFailed = ScanFailed,
            ScanPaused = ScanPaused,
            ScanError = ScanError,
            CurrentlyScannedUrl = CurrentlyScannedUrl,
            FoundMediaCount = FoundMedia.Count,
            SelectedMediaCount = FoundMedia.Count(media => media.Selected),
            Media = FoundMedia.Select(media => new ScannedMediaDTO
            {
                Id = media.Id,
                Name = media.OverrideName ?? media.DownloadUrl,
                DownloadUrl = media.DownloadUrl,
                ThumbnailUrl = media.ThumbnailUrl,
                Selected = media.Selected,
                Referrer = media.Referrer,
                MediaType = media.MediaType,
            }).ToList(),
        };
    }

    public ScannedCollectionStatusDTO GetStatusDTO()
    {
        return new ScannedCollectionStatusDTO
        {
            Id = Id,
            Name = Name,
            ScanFailed = ScanFailed,
            ScanPaused = ScanPaused,
            ScanError = ScanError,
            CurrentlyScannedUrl = CurrentlyScannedUrl,
            FoundMediaCount = FoundMedia.Count,
            SelectedMediaCount = FoundMedia.Count(media => media.Selected),
        };
    }
}

public class FoundMedia : UpdateableModel
{
    public FoundMedia(string downloadUrl)
    {
        DownloadUrl = downloadUrl;
    }

    [Key]
    public long Id { get; set; }

    public long ScannedCollectionId { get; set; }
    public ScannedCollection ScannedCollection { get; set; } = null!;

    [MaxLength(4096)]
    public string DownloadUrl { get; set; }

    [MaxLength(4096)]
    public string CanonicalUrl { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? ThumbnailUrl { get; set; }

    [MaxLength(512)]
    public string? OverrideName { get; set; }

    public bool Selected { get; set; } = true;

    public MediaType? MediaType { get; set; }

    [MaxLength(4096)]
    public string? Referrer { get; set; }

    [MaxLength(16384)]
    public string? Cookies { get; set; }

    [MaxLength(16384)]
    public string? ImpersonationHeaders { get; set; }

    [MaxLength(256)]
    public string? Hash { get; set; }

    [MaxLength(1024)]
    public string? LocalThumbnailFilePath { get; set; }

    [MaxLength(1024)]
    public string? LocalFullFilePath { get; set; }

    public ICollection<AppliedTag> AppliedTags { get; set; } = new List<AppliedTag>();

    public List<string> UnparsedTags { get; set; } = new();
}
