using System.Globalization;
using System.Text.Json.Nodes;
using Backend.Utilities;

namespace Backend.Models;

/// <summary>
///   A stream that has been detected from a media file by <see cref="FileProbe"/>
/// </summary>
public abstract class MediaStream(Codec codecType)
{
    public Codec Codec { get; } = codecType;

    /// <summary>
    ///   Parses a single stream loaded from JSON
    /// </summary>
    /// <returns>A created stream object</returns>
    public static MediaStream Parse(int containerBitRate, JsonNode stream)
    {
        // Assume data is correct and throw exceptions if it isn't
        var codecName = (string?)stream["codec_name"];

        if (codecName == null)
        {
            codecName = (string?)stream["codec_tag_string"] ?? throw new Exception("Unknown stream name / tag");
        }

        if (DataStream.IsData(codecName) && (string)stream["codec_type"]! == "data" ||
            (string)stream["codec_type"]! == "subtitle" || (string)stream["codec_type"]! == "attachment")
        {
            return new DataStream(codecName, (string)stream["codec_type"]!);
        }

        if (!Enum.TryParse<Codec>(codecName, true, out var codec))
        {
            throw new Exception("Unknown codec: " + codecName);
        }

        // This is used here already to determine what type the codec is without code repetition
        var action = codec.GetCategory();

        switch (action)
        {
            case CodecCategory.Video:
                return new VideoStream(codec, containerBitRate, stream);
            case CodecCategory.Audio:
                return new AudioStream(codec, containerBitRate, stream);
            case CodecCategory.Data:
                throw new InvalidOperationException("Data streams should have been filtered out earlier");
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

public class AudioStream : MediaStream
{
    public AudioStream(Codec codec, int containerBitRate, JsonNode streamData) : base(codec)
    {
        try
        {
            Channels = (int)streamData["channels"]!;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Audio stream has no channels", e);
        }

        if (streamData["bit_rate"] != null)
        {
            BitRate = int.Parse((string)streamData["bit_rate"]!, CultureInfo.InvariantCulture);
        }
        else
        {
            // We don't convert audio rates in this program, so this should be fine
            BitRate = containerBitRate;
        }
    }

    public int Channels { get; set; }
    public int BitRate { get; set; }
}

public class VideoStream : MediaStream
{
    public VideoStream(Codec codec, int containerBitRate, JsonNode streamData) : base(codec)
    {
        Width = (int)streamData["width"]!;
        Height = (int)streamData["height"]!;

        if (streamData["bit_rate"] != null)
        {
            BitRate = int.Parse((string)streamData["bit_rate"]!, CultureInfo.InvariantCulture);
        }
        else
        {
            BitRate = containerBitRate;
        }

        // Some streams *might* need to use "avg_frame_rate"
        var parts = ((string)streamData["r_frame_rate"]!).Split('/', 2);

        FramesPerSecond = int.Parse(parts[0]) / (double)int.Parse(parts[1]);

        // Mjpeg seems to often be a separate stream that we don't want. bmp seems to also be preview images
        if (codec is Codec.Mjpeg or Codec.Bmp or Codec.Png)
            IsPotentiallyUndesired = true;
    }

    public int Width { get; set; }
    public int Height { get; set; }

    public int BitRate { get; set; }

    public double FramesPerSecond { get; set; }

    /// <summary>
    ///   Some "video" streams are actually just thumbnails or preview images we don't really want
    /// </summary>
    public bool IsPotentiallyUndesired { get; set; }
}

/// <summary>
///   A data type stream that just wants to be copied
/// </summary>
public class DataStream : MediaStream
{
    public DataStream(string codecName, string codecType) : base(Codec.Data)
    {
        if (codecName == "bin_data")
        {
            RequireSameContainerFormat = true;
        }
        else if (codecName == "rtp " || codecName == "amf0" || (codecName == "mp4s" && codecType == "data") ||
                 codecName == "mebx")
        {
            // Sometimes mp4s seems to be handled as subtitles, so make sure the type is data before discarding it

            Discard = true;
        }
        else if (codecName == "tmcd" || codecName == "timed_id3")
        {
            // Time-code data doesn't seem required
            Discard = true;
        }
    }

    public bool RequireSameContainerFormat { get; init; }

    public bool Discard { get; set; }

    /// <summary>
    ///   Determines if a stream codec type is safely copyable data
    /// </summary>
    /// <param name="codecName">Name of the stream format</param>
    /// <returns>True if it is data</returns>
    public static bool IsData(string codecName)
    {
        // All subtitles. Advanced SSA subtitle is abbreviated as "ass"
        if (codecName == "dvb_subtitle" || codecName == "subrip" || codecName == "hdmv_pgs_subtitle" ||
            codecName == "ass")
        {
            return true;
        }

        // Font data that's kept for subtitles
        if (codecName == "ttf")
            return true;

        if (codecName == "epg")
            return true;

        if (codecName == "dvb_teletext")
            return true;

        if (codecName == "dvd_nav_packet")
            return true;

        // Streaming packet data that can be discarded
        if (codecName == "rtp " || codecName == "amf0")
            return true;

        // Time-code data
        if (codecName == "tmcd" || codecName == "timed_id3")
            return true;

        // Random other data
        if (codecName == "mebx")
            return true;

        // This seems to be sometimes useless data that ffmpeg can't even decode so this can be discarded
        // But maybe this can be subtitles in some contexts
        if (codecName == "mp4s")
            return true;

        if (codecName == "bin_data")
        {
            return true;
        }

        return false;
    }
}
