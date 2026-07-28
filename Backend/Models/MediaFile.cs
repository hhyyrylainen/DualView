using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   Concrete stored media file, not used directly but with <see cref="ConfiguredMedia"/>
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

    [MaxLength(256)]
    public string HashSha3 { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

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

    public long? DerivedFromImageId { get; set; }

    public MediaFile? DerivedFrom { get; set; }

    public ICollection<MediaFile> DerivedImages { get; set; } = new List<MediaFile>();

    /// <summary>
    ///   Actual configurations of this file that should be used
    /// </summary>
    public ICollection<ConfiguredMedia> Configurations { get; set; } = new List<ConfiguredMedia>();

    public string PathRelativeToStorage()
    {
        return $"originalMedia/{HashSha3[..2]}/{HashSha3[2..4]}/{HashSha3[4..]}{Path.GetExtension(OriginalFileName)}";
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
            DerivedFromImageId = DerivedFromImageId,
        };
    }
}
