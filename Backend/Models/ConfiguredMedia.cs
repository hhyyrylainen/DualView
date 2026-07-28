using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using Backend.Utilities;

namespace Backend.Models;

/// <summary>
///   A media configuration. Media is stored by unique media files and then configurations which apply filters like
///   cropping and masking
/// </summary>
/// <remarks>
///   <para>
///     NOTE: The uniqueness constraint for prime media is now configured in AppDbContext using a filtered (partial)
///     unique index for SQLite: UNIQUE ON MediaFileId WHERE Prime = 1
///   </para>
/// </remarks>
public class ConfiguredMedia : UpdateableModel, ISoftDelete, IDTOProvider<ConfiguredMediaDTO>,
    IInfoProvider<ConfiguredMediaInfo>, IConfiguredMedia
{
    public ConfiguredMedia(string name)
    {
        Name = name;
    }

    [Key] public long Id { get; set; }

    /// <summary>
    ///   When prime, this is an unmodified version of the media file, and there may just be one prime per media
    /// </summary>
    public bool Prime { get; set; }

    /// <summary>
    ///   Name from the file. This is here to allow sorting directory entries by name without massively needing to join
    ///   tables for that operation.
    /// </summary>
    [MaxLength(100)]
    public string Name { get; set; }

    public long MediaFileId { get; set; }

    public MediaFile MediaFile { get; set; } = null!;

    [NotMapped]
    public IMediaFile? MediaFileBase
    {
        get => MediaFile;
        set => MediaFile = (MediaFile?)value ?? throw new ArgumentNullException(nameof(value));
    }

    // Copied info from the primary media file
    public MediaType MediaType { get; set; }

    public bool IsDeleted { get; set; }

    // Cropping pixels out (when 0 there's no cropping)
    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    /// <summary>
    ///   Rotation of the image in degrees in a clockwise direction.
    /// </summary>
    public int Rotation { get; set; }

    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }

    // Length cropping (if set specifies the start and end frames of the animation)
    public int? CropStart { get; set; }
    public int? CropEnd { get; set; }

    public float Scale { get; set; } = 1;

    // Calculated properties
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameCount { get; set; }
    public float FramesPerSecond { get; set; }

    public ICollection<MediaStorageFolder> InFolders { get; set; } = new List<MediaStorageFolder>();

    /// <summary>
    ///   If set to true, then there is a mask file associated with this media that can be loaded by the media
    ///   controller for viewing and applied to workflows when using this media.
    /// </summary>
    public bool MaskEnabled { get; set; }

    /// <summary>
    ///   Creates a not-too-long name from the original name by truncating it if necessary
    /// </summary>
    /// <param name="originalName">Original name</param>
    /// <param name="keepExtension">If true, keep the file extension in the adjusted name</param>
    /// <returns>Adjusted name</returns>
    public static string AdjustedNameFromOriginal(string originalName, bool keepExtension = false)
    {
        const int lengthLimit = 100;

        if (!keepExtension)
        {
            var name = Path.GetFileNameWithoutExtension(originalName);

            if (name.Length <= lengthLimit)
                return name;

            return name.Substring(0, lengthLimit);
        }

        if (originalName.Length <= lengthLimit)
            return originalName;

        var extension = Path.GetExtension(originalName);

        return originalName.Substring(0, lengthLimit - extension.Length) + extension;
    }

    public ConfiguredMediaDTO GetDTO()
    {
        return new ConfiguredMediaDTO(Name)
        {
            Id = Id,
            Prime = Prime,
            MediaFileId = MediaFileId,
            MediaType = MediaType,
            IsDeleted = IsDeleted,
            CropStart = CropStart,
            CropEnd = CropEnd,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom,
            Scale = Scale,
            Width = Width,
            Height = Height,
            FrameCount = FrameCount,
            FramesPerSecond = FramesPerSecond,
            Rotation = Rotation,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            // This ? is important as the navigation may not be loaded from EF
            MediaFile = MediaFile?.GetDTO(),
            MaskEnabled = MaskEnabled,
        };
    }

    public ConfiguredMediaInfo GetInfo()
    {
        // TODO: name for a directory listing purposes?
        return new ConfiguredMediaInfo(Name, Id, Prime, MediaFileId, MediaType, Width, Height, MaskEnabled);
    }

    public bool UpdateFromClient(ConfiguredMediaDTO other)
    {
        if (Prime)
            throw new InvalidOperationException("Cannot modify a prime media");

        bool changes = false;

        if (Name != other.Name)
        {
            Name = other.Name;
            changes = true;
        }

        if (other.CropLeft < 0)
            other.CropLeft = 0;

        if (other.CropTop < 0)
            other.CropTop = 0;

        if (other.CropRight < 0)
            other.CropRight = 0;

        if (other.CropBottom < 0)
            other.CropBottom = 0;

        if (CropLeft != other.CropLeft)
        {
            CropLeft = other.CropLeft;
            changes = true;
        }

        if (CropTop != other.CropTop)
        {
            CropTop = other.CropTop;
            changes = true;
        }

        if (CropRight != other.CropRight)
        {
            CropRight = other.CropRight;
            changes = true;
        }

        if (CropBottom != other.CropBottom)
        {
            CropBottom = other.CropBottom;
            changes = true;
        }

        if (CropStart != other.CropStart)
        {
            CropStart = other.CropStart;
            changes = true;
        }

        if (CropEnd != other.CropEnd)
        {
            CropEnd = other.CropEnd;
            changes = true;
        }

        if (Math.Abs(Scale - other.Scale) > 0.0001f)
        {
            Scale = other.Scale;
            changes = true;
        }

        if (Rotation != other.Rotation)
        {
            Rotation = other.Rotation;
            changes = true;
        }

        if (FlipHorizontal != other.FlipHorizontal)
        {
            FlipHorizontal = other.FlipHorizontal;
            changes = true;
        }

        if (FlipVertical != other.FlipVertical)
        {
            FlipVertical = other.FlipVertical;
            changes = true;
        }

        return changes;
    }
}