namespace DualView.Shared.Models;

public class RequiredToolCheckResult
{
    public bool ImageMagickAvailable { get; set; }

    public bool FfmpegAvailable { get; set; }

    public string? FfmpegVersion { get; set; }
}
