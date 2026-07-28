using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class ConfiguredMediaDTO(string name) : IConfiguredMedia
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = name;

    public long Id { get; set; }
    public bool Prime { get; set; }
    public long MediaFileId { get; set; }
    public MediaType MediaType { get; set; }
    public bool IsDeleted { get; set; }
    public int? CropStart { get; set; }
    public int? CropEnd { get; set; }
    public float Scale { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }
    public float FramesPerSecond { get; set; }

    public MediaFileDTO? MediaFile { get; set; }

    [JsonIgnore]
    public IMediaFile? MediaFileBase
    {
        get => MediaFile;
        set => MediaFile = (MediaFileDTO?)value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Rotation { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    /// <summary>
    ///   This is the mask-enabled state. Only allowed to be set with the mask updating API!
    /// </summary>
    public bool MaskEnabled { get; set; }
}

public interface IConfiguredMediaInfo
{
    string Name { get; }
    long Id { get; set; }
    bool Prime { get; set; }
    long MediaFileId { get; set; }
    MediaType MediaType { get; set; }
    int Width { get; set; }
    int Height { get; set; }
    bool MaskEnabled { get; }
}

public class ConfiguredMediaInfo : IConfiguredMediaInfo
{
    [JsonConstructor]
    public ConfiguredMediaInfo(string name, long id, bool prime, long mediaFileId, MediaType mediaType, int width,
        int height, bool maskEnabled)
    {
        Name = name;
        Id = id;
        Prime = prime;
        MediaFileId = mediaFileId;
        MediaType = mediaType;
        Width = width;
        Height = height;
        MaskEnabled = maskEnabled;
    }

    public ConfiguredMediaInfo(IConfiguredMedia media) : this(media.Name, media.Id, media.Prime, media.MediaFileId,
        media.MediaType, media.Width, media.Height, media.MaskEnabled)
    {
    }

    public string Name { get; }
    public long Id { get; set; }
    public bool Prime { get; set; }
    public long MediaFileId { get; set; }
    public MediaType MediaType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool MaskEnabled { get; set; }
}
