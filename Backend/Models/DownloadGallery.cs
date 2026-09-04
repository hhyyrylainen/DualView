using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   Provenance record describing the remote gallery a downloaded/imported item came from. This is just a historical
///   record of where content came from.
///   This is not the persisted scanner queue; use <see cref="ScannedCollection"/> for that.
/// </summary>
[Index(nameof(GalleryUrl), IsUnique = true)]
public class DownloadGallery : UpdateableModel, ISoftDelete, IDTOProvider<DownloadGalleryDTO>
{
    public DownloadGallery(string galleryUrl)
    {
        GalleryUrl = galleryUrl;
    }

    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(1024)]
    public string GalleryUrl { get; set; }

    [MaxLength(1024)]
    public string? TargetPath { get; set; }

    [MaxLength(200)]
    public string? GalleryName { get; set; }

    public bool IsDownloaded { get; set; }

    [MaxLength(4096)]
    public string? TagsString { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<MediaImportInfo> AssociatedImports { get; set; } = new List<MediaImportInfo>();

    public DownloadGalleryDTO GetDTO()
    {
        return new DownloadGalleryDTO(GalleryUrl)
        {
            Id = Id,
            TargetPath = TargetPath,
            GalleryName = GalleryName,
            IsDownloaded = IsDownloaded,
            TagsString = TagsString,
            IsDeleted = IsDeleted,
        };
    }
}
