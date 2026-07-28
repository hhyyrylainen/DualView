using System.Diagnostics;
using DualView.Shared.Models;
using ImageMagick;

namespace DualView.Server.Services;

public class SystemCheck : ISystemCheck
{
    private readonly ILogger<SystemCheck> logger;

    private DateTime lastCheck = DateTime.MinValue;
    private RequiredToolCheckResult? lastResult;

    public SystemCheck(ILogger<SystemCheck> logger)
    {
        this.logger = logger;
    }

    public async Task<RequiredToolCheckResult> CheckRequiredTools(CancellationToken cancellation)
    {
        if (lastResult != null && lastCheck > DateTime.UtcNow.AddSeconds(-10))
        {
            return lastResult;
        }

        var result = new RequiredToolCheckResult();

        // We link against Magick.NET, so just use it to see if it fails
        try
        {
            using var image = new MagickImage();
            result.ImageMagickAvailable = true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load Magick.NET");
            result.ImageMagickAvailable = false;
        }

        // Then run ffmpeg to see if it works
        var runInfo = new ProcessStartInfo("ffmpeg");
        runInfo.ArgumentList.Add("-version");
        runInfo.RedirectStandardOutput = true;

        try
        {
            var processResults = Process.Start(runInfo) ?? throw new Exception("Failed to start ffmpeg");

            var saw264 = false;
            var sawVersion = false;

            while (true)
            {
                var line = await processResults.StandardOutput.ReadLineAsync(cancellation);

                if (line == null)
                    break;

                // Get the first line with the "version" word to get the version
                if (line.Contains("version") && !sawVersion)
                {
                    result.FfmpegVersion = line.Trim();
                    sawVersion = true;
                }

                if (line.Contains("libx264"))
                {
                    saw264 = true;
                }
            }

            await processResults.WaitForExitAsync(cancellation);

            result.FfmpegAvailable = processResults.ExitCode == 0;

            // We assume if ffmpeg is available, there's also ffprobe

            result.FfmpegVersion ??= "No version info available";

            // Check that the output contains "x264", otherwise write the version number as missing H264 support
            if (!saw264)
            {
                result.FfmpegVersion += result.FfmpegVersion.Split("Copyright")[0] + " (missing H264 support)";
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to run ffmpeg");
            result.FfmpegAvailable = false;
        }

        lastResult = result;
        lastCheck = DateTime.UtcNow;

        return result;
    }
}
