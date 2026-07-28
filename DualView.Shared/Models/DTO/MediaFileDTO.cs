using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class MediaFileDTO(string originalFileName, string hashSha3) : IMediaFile
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string OriginalFileName { get; set; } = originalFileName;

    [MaxLength(256)]
    [Required]
    public string HashSha3 { get; set; } = hashSha3;

    public DateTime ImportedAt { get; set; }

    public bool IsDeleted { get; set; }

    public bool Keep { get; set; }

    public MediaType MediaType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }

    public float FramesPerSecond { get; set; }

    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    public bool IsFavorited { get; set; }
    public int Stars { get; set; }

    public long? ParentMediaId { get; set; }
}

public interface IMediaFile
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string OriginalFileName { get; set; }

    [MaxLength(256)]
    [Required]
    public string HashSha3 { get; set; }

    public DateTime ImportedAt { get; set; }

    public bool IsDeleted { get; set; }

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

    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    public bool IsFavorited { get; set; }
    public int Stars { get; set; }

    public long? ParentMediaId { get; set; }
}
