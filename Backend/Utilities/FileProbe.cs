using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Models;
using ImageMagick;

namespace Backend.Utilities;

/// <summary>
///   Probes files for media details
/// </summary>
public static class FileProbe
{
    public static async Task<MediaFileInfo> ProbeVideoAsync(string path, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ffprobe");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(Path.GetFullPath(path));

        startInfo.RedirectStandardOutput = true;

        var process = Process.Start(startInfo);

        if (process == null)
            throw new ApplicationException("Failed to start ffprobe");

        // Make sure too long output doesn't get stuck in the buffer by reading it first before waiting
        var outputText = await process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Console.WriteLine(outputText);
            throw new ApplicationException("Failed to run ffprobe");
        }

        var rawData = JsonSerializer.Deserialize<JsonObject>(outputText) ??
                      throw new JsonException("Failed to parse ffprobe output");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return ParseJsonData(rawData, path);
        }
        catch (Exception e)
        {
            Console.WriteLine(outputText);
            throw new ApplicationException("Probing a file failed", e);
        }
    }

    private static MediaFileInfo ParseJsonData(JsonObject rawData, string path)
    {
        // This uses a bunch of null suppression as a format is assumed to be correct, and an exception is caught by
        // our caller if not
        var parsed = new MediaFileInfo(path, ParseDuration((string)rawData["format"]!["duration"]!));

        // Video streams often don't have their own bitrate, so we need to get the overall one here
        var bitRate = int.Parse((string)rawData["format"]!["bit_rate"]!);

        foreach (var stream in rawData["streams"]!.AsArray())
        {
            if (stream == null)
                throw new Exception("Null stream in stream list");

            parsed.AddStream(MediaStream.Parse(bitRate, stream));
        }

        return parsed;
    }

    private static double ParseDuration(string duration)
    {
        return Double.Parse(duration, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///   Info of a probed file by <see cref="FileProbe"/>
    /// </summary>
    public class MediaFileInfo
    {
        public MediaFileInfo(string path, double duration)
        {
            Path = System.IO.Path.GetFullPath(path);
            Duration = duration;

            var info = new FileInfo(Path);

            if (!info.Exists)
                throw new ArgumentException("File does not exist");

            SizeInBytes = info.Length;
        }

        public List<MediaStream> Streams { get; } = new();

        public long SizeInBytes { get; }

        public string Path { get; }

        public double Duration { get; }

        public bool HasDiscardableVideoStreams { get; private set; }

        /// <summary>
        ///   Gets the first video stream (if one exists)
        /// </summary>
        public VideoStream? VideoStream
        {
            get
            {
                if (HasDiscardableVideoStreams)
                    return Streams.OfType<VideoStream?>().First(s => s is { IsPotentiallyUndesired: false });

                return Streams.First(s => s is VideoStream) as VideoStream;
            }
        }

        public AudioStream? FirstAudioStream => Streams.FirstOrDefault(s => s is AudioStream) as AudioStream;

        public int VideoStreamCount => Streams.Count(s => s is VideoStream);
        public int AudioStreamCount => Streams.Count(s => s is AudioStream);
        public int DataStreamCount => Streams.Count(s => s is DataStream);

        public bool IsAudioOnly => VideoStreamCount < 1 && AudioStreamCount > 0;

        public void AddStream(MediaStream stream)
        {
            if (stream is VideoStream videoStream)
            {
                bool canAdd = true;

                foreach (var mediaStream in Streams)
                {
                    if (mediaStream is VideoStream existingVideoStream)
                    {
                        // TODO: should only one undesired stream be allowed?
                        if (existingVideoStream.IsPotentiallyUndesired && !videoStream.IsPotentiallyUndesired)
                        {
                            continue;
                        }

                        // Can have multiple streams when one is undesired
                        if (videoStream.IsPotentiallyUndesired)
                            continue;

                        canAdd = false;
                        break;
                    }
                }

                if (!canAdd)
                    throw new InvalidOperationException("Multiple non-discarded video streams aren't supported");

                if (videoStream.IsPotentiallyUndesired)
                {
                    HasDiscardableVideoStreams = true;
                }
            }
            else if (stream is AudioStream audioStream)
            {
            }
            else if (stream is DataStream dataStream)
            {
                /*if (dataStream.RequireSameContainerFormat)
                {
                    ShouldKeepOriginalContainer = true;
                }*/
            }

            Streams.Add(stream);
        }

        public bool DetectShouldDiscardStreams()
        {
            bool discardPossible = true;
            bool shouldDiscard = HasDiscardableVideoStreams;

            foreach (var stream in Streams)
            {
                // Only data streams are considered for discarding currently
                if (stream is not DataStream dataStream)
                    continue;

                if (dataStream.Discard)
                {
                    shouldDiscard = true;
                }
                else
                {
                    // Non-discardable data streams currently disallow discarding any streams as such a mixed stream
                    // mapping scenario is not coded
                    discardPossible = false;
                }
            }

            return shouldDiscard && discardPossible;
        }

        /// <summary>
        ///   Returns true if a video stream should be ignored. Index must always be valid (less than
        ///   <see cref="VideoStreamCount"/>)
        /// </summary>
        /// <param name="index">Index of the stream</param>
        /// <returns>True if the stream should be ignored when converting and not mapped</returns>
        public bool IsVideoStreamDiscarded(int index)
        {
            var stream = GetVideoStream(index);

            return stream.IsPotentiallyUndesired;
        }

        /// <summary>
        ///   Gets a video stream with index or throws if not ofund
        /// </summary>
        /// <returns>The video stream</returns>
        public VideoStream GetVideoStream(int index)
        {
            return Streams.OfType<VideoStream>().ElementAt(index);
        }
    }

    public static string? GetExtensionForFormat(MagickFormat format)
    {
        return format switch
        {
            MagickFormat.Png => ".png",
            MagickFormat.Gif => ".gif",
            MagickFormat.Bmp => ".bmp",
            MagickFormat.Jpeg => ".jpg",
            MagickFormat.WebP => ".webp",
            MagickFormat.Tiff => ".tiff",
            _ => null,
        };
    }
}
