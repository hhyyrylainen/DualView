using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class ConfiguredMediaInfo(
    string name,
    long id,
    long mediaFileId,
    MediaType mediaType,
    int width,
    int height) : IConfiguredMediaInfo
{
    public string Name { get; set; } = name;
    public long Id { get; set; } = id;
    public long MediaFileId { get; set; } = mediaFileId;
    public MediaType MediaType { get; set; } = mediaType;
    public int Width { get; set; } = width;
    public int Height { get; set; } = height;

    public bool IsFolder { get; set; }
    public bool IsCollection { get; set; }

    public ConfiguredMediaInfo(MediaFileDTO media) : this(media.OriginalFileName, media.Id, media.Id,
        media.MediaType, media.Width, media.Height)
    {
    }
}

public class ConfiguredMediaDTO(string name, string hashSha3)
    : MediaFileDTO(name, hashSha3), IConfiguredMediaInfo, IUpdateableModel
{
    public string Name => OriginalFileName;
    public long MediaFileId => Id;
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
    long MediaFileId { get; }
    MediaType MediaType { get; }
    int Width { get; }
    int Height { get; }

    bool IsFolder { get; }
    bool IsCollection { get; }
}
