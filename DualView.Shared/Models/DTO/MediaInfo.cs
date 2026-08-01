using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class ConfiguredMediaInfo(
    string name,
    long id,
    bool prime,
    long mediaFileId,
    MediaType mediaType,
    int width,
    int height,
    bool maskEnabled) : IConfiguredMediaInfo
{
    public string Name { get; set; } = name;
    public long Id { get; set; } = id;
    public bool Prime { get; set; } = prime;
    public long MediaFileId { get; set; } = mediaFileId;
    public MediaType MediaType { get; set; } = mediaType;
    public int Width { get; set; } = width;
    public int Height { get; set; } = height;
    public bool MaskEnabled { get; set; } = maskEnabled;

    public bool IsFolder { get; set; }
    public bool IsCollection { get; set; }

    public ConfiguredMediaInfo(MediaFileDTO media) : this(media.OriginalFileName, media.Id, true, media.Id,
        media.MediaType, media.Width, media.Height, false)
    {
    }
}

public class ConfiguredMediaDTO(string name, string hashSha3)
    : MediaFileDTO(name, hashSha3), IConfiguredMediaInfo, IUpdateableModel
{
    public string Name => OriginalFileName;
    public bool Prime => true;
    public long MediaFileId => Id;
    public bool MaskEnabled => false;
    public bool IsFolder => false;
    public bool IsCollection => false;
    public float Scale { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public MediaFileDTO MediaFile => this;

    public ConfiguredMediaDTO(MediaFileDTO media) : this(media.OriginalFileName, media.HashSha3)
    {
        Id = media.Id;
        ImportedAt = media.ImportedAt;
        IsDeleted = media.IsDeleted;
        MediaType = media.MediaType;
        Width = media.Width;
        Height = media.Height;
        FrameCount = media.FrameCount;
        FramesPerSecond = media.FramesPerSecond;
        CropLeft = media.CropLeft;
        CropTop = media.CropTop;
        CropRight = media.CropRight;
        CropBottom = media.CropBottom;
        ParentMediaId = media.ParentMediaId;
        ImportedAt = media.ImportedAt;
    }

    public void RefreshDerivedStatistics()
    {
    }
}

public interface IConfiguredMediaInfo
{
    string Name { get; }
    long Id { get; }
    bool Prime { get; }
    long MediaFileId { get; }
    MediaType MediaType { get; }
    int Width { get; }
    int Height { get; }
    bool MaskEnabled { get; }

    bool IsFolder { get; }
    bool IsCollection { get; }
}
