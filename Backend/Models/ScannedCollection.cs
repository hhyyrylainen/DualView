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

    /// <summary>
    ///   NOTE: the name says "unrecognized tags" but it's actually a list of all tags that will be applied once
    ///   accepted. A more apt name would be "TagsToApply".
    /// </summary>
    [MaxLength(4096)]
    public string UnrecognizedTags { get; set; } = string.Empty;

    [MaxLength(1000000)]
    public string ScannerState { get; set; } = string.Empty;

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
                Name = GetImportFileName(media),
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

    private static string GetImportFileName(FoundMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.OverrideName) && Path.GetExtension(media.OverrideName).Length > 0)
            return media.OverrideName;

        if (Uri.TryCreate(media.DownloadUrl, UriKind.Absolute, out var uri) &&
            Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } fileName)
        {
            return fileName;
        }

        return media.DownloadUrl;
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

    // If these are set, the thumbnail is a spritesheet that needs cropping
    public int? ThumbnailCropX { get; set; }
    public int? ThumbnailCropY { get; set; }
    public int? ThumbnailCropWidth { get; set; }
    public int? ThumbnailCropHeight { get; set; }

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

    /// <summary>
    ///   Raw tags discovered for this media, parsed and applied when the scan is accepted.
    /// </summary>
    public List<string> UnparsedTags { get; set; } = new();
}
