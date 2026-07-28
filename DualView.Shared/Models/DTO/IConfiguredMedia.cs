namespace DualView.Shared.Models.DTO;

public interface IConfiguredMedia : IConfiguredMediaInfo
{
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

    public float Scale { get; set; }

    // Calculated properties
    public int FrameCount { get; set; }
    public float FramesPerSecond { get; set; }

    public IMediaFile? MediaFileBase { get; set; }
}

public static class ConfiguredMediaExtensions
{
    extension(IConfiguredMedia media)
    {
        public void RefreshDerivedStatistics()
        {
            if (media.MediaFileBase == null)
                throw new InvalidOperationException("Media navigation must be loaded");

            media.Width = media.MediaFileBase.Width;
            media.Height = media.MediaFileBase.Height;
            media.FrameCount = media.MediaFileBase.FrameCount;
            media.FramesPerSecond = media.MediaFileBase.FramesPerSecond;

            media.FrameCount -= media.CropStart ?? 0;
            media.FrameCount -= media.CropEnd ?? 0;
            media.FrameCount = Math.Max(1, media.FrameCount);

            media.Width -= media.CropLeft + media.CropRight;
            media.Height -= media.CropTop + media.CropBottom;

            media.Width = (int)(media.Width * media.Scale);
            media.Height = (int)(media.Height * media.Scale);
        }
    }
}
