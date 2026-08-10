using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   Concrete stored media file
/// </summary>
[Index(nameof(Hash), IsUnique = true)]
public class MediaFile : UpdateableModel, IDTOProvider<MediaFileDTO>, IMediaFile, ISoftDelete
{
    public MediaFile(string originalFileName, string hash)
    {
        OriginalFileName = originalFileName;
        NameLowerCase = originalFileName.ToLowerInvariant();
        Hash = hash;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(256)]
    public string OriginalFileName
    {
        get;
        set
        {
            field = value;
            NameLowerCase = value.ToLowerInvariant();
        }
    }

    [MaxLength(256)]
    public string NameLowerCase { get; set; }

    /// <summary>
    ///   Base64-encoded sha hash of the file. Note must be calculated with MediaHash.CalculateMediaHash
    /// </summary>
    [MaxLength(256)]
    public string Hash { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastViewed { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsTemporary { get; set; }

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

    // Rating properties
    public bool IsFavorited { get; set; }

    [Range(-1, 5)]
    public int Stars { get; set; } = -1;

    public long? ParentMediaId { get; set; }

    public MediaFile? ParentMedia { get; set; }

    public ICollection<MediaFile> Children { get; set; } = new List<MediaFile>();

    public ICollection<CollectionItem> InCollections { get; set; } = new List<CollectionItem>();

    public ICollection<AppliedTag> AppliedTags { get; set; } = new List<AppliedTag>();

    public ICollection<UploadSectionItem> InUploadSections { get; set; } = new List<UploadSectionItem>();

    public MediaImportInfo? ImportInfo { get; set; }

    public string PathRelativeToStorage()
    {
        return $"originalMedia/{Hash[..2]}/{Hash[2..4]}/{Hash[4..]}{Path.GetExtension(OriginalFileName)}";
    }

    public string CroppedPathRelativeToStorage()
    {
        var extension = Path.GetExtension(OriginalFileName);
        return $"originalMedia/{Hash[..2]}/{Hash[2..4]}/{Hash[4..]}_cropped{extension}";
    }

    public MediaFileDTO GetDTO()
    {
        return new MediaFileDTO(OriginalFileName, Hash)
        {
            Id = Id,
            ImportedAt = ImportedAt,
            IsDeleted = IsDeleted,
            IsTemporary = IsTemporary,
            MediaType = MediaType,
            Width = Width,
            Height = Height,
            FrameCount = FrameCount,
            FramesPerSecond = FramesPerSecond,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom,
            IsFavorited = IsFavorited,
            Stars = Stars,
            ParentMediaId = ParentMediaId,
        };
    }
}
