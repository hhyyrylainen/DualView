using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   Concrete stored media file
/// </summary>
[Index(nameof(HashSha3), IsUnique = true)]
public class MediaFile : IDTOProvider<MediaFileDTO>, IMediaFile
{
    public MediaFile(string originalFileName, string hashSha3)
    {
        OriginalFileName = originalFileName;
        HashSha3 = hashSha3;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string OriginalFileName { get; set; }

    // TODO: add a lowercase version of the original file name for searching

    [MaxLength(256)]
    public string HashSha3 { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    // TODO: remove this as DualView won't use it
    /// <summary>
    ///   When set to on, won't be deleted automatically from job results when the jobs expire
    /// </summary>
    public bool Keep { get; set; }

    // Media info
    public MediaType MediaType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }

    /// <summary>
    ///   This is a float as framerate is not always exact. -1 if not animated.
    /// </summary>
    public float FramesPerSecond { get; set; }

    // Cropping properties
    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    public long? ParentMediaId { get; set; }

    public MediaFile? ParentMedia { get; set; }

    public ICollection<MediaFile> Children { get; set; } = new List<MediaFile>();

    public ICollection<CollectionItem> InCollections { get; set; } = new List<CollectionItem>();

    public string PathRelativeToStorage()
    {
        return $"originalMedia/{HashSha3[..2]}/{HashSha3[2..4]}/{HashSha3[4..]}{Path.GetExtension(OriginalFileName)}";
    }

    public string CroppedPathRelativeToStorage()
    {
        var extension = Path.GetExtension(OriginalFileName);
        return $"originalMedia/{HashSha3[..2]}/{HashSha3[2..4]}/{HashSha3[4..]}_cropped{extension}";
    }

    public MediaFileDTO GetDTO()
    {
        return new MediaFileDTO(OriginalFileName, HashSha3)
        {
            Id = Id,
            ImportedAt = ImportedAt,
            IsDeleted = IsDeleted,
            Keep = Keep,
            MediaType = MediaType,
            Width = Width,
            Height = Height,
            FrameCount = FrameCount,
            FramesPerSecond = FramesPerSecond,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom,
            ParentMediaId = ParentMediaId,
        };
    }
}
