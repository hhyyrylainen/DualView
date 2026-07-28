namespace Backend.Models;

public enum Codec
{
    // Video codecs
    Mpeg1Video,
    Mpeg2Video,

    Vp6F,

    Flv1,
    H264,
    Mpeg4,
    Msmpeg4V3,
    Hevc,
    Vp8,
    Vp9,

    Wmv1,
    Wmv2,
    Wmv3,
    Vc1,
    QtRle,

    // Often undesired extra video stream that needs to be discarded (these are like thumbnail streams or something
    // that doesn't seem to affect the result)
    Mjpeg,
    Bmp,
    Png,

    // Audio codecs that are so bad that they should be converted
    Mp2,
    Wmav1,
    Wmav2,
    NellyMoser,

    // ReSharper disable once InconsistentNaming
    Adpcm_swf,

    // ReSharper disable once InconsistentNaming
    Adpcm_Ms,

    // ReSharper disable once InconsistentNaming
    Pcm_u8,

    // ReSharper disable once InconsistentNaming
    Pcm_s16be,

    // ReSharper disable once InconsistentNaming
    Pcm_s24Le,

    Av1,
    Aac,
    Vorbis,
    Opus,
    Mp3,
    Wmapro,
    Ac3,
    Flac,
    Eac3,

    // Has weird channel layouts that are not convertible
    Dts,

    // Probably uncompressed audio, left as-is
    // ReSharper disable once InconsistentNaming
    Pcm_S16Le,

    // Data codecs
    Data,
}

public enum CodecCategory
{
    Video,
    Audio,
    Data,
}

public static class CodecExtensions
{
    public static CodecCategory GetCategory(this Codec codec)
    {
        return codec switch
        {
            Codec.Mpeg1Video or Codec.Mpeg2Video or Codec.Vp6F or Codec.Flv1 or Codec.H264 or Codec.Mpeg4
                or Codec.Msmpeg4V3 or Codec.Hevc or Codec.Vp8 or Codec.Vp9 or Codec.Wmv1 or Codec.Wmv2 or Codec.Wmv3
                or Codec.Vc1 or Codec.QtRle or Codec.Mjpeg or Codec.Bmp or Codec.Png
                or Codec.Av1 => CodecCategory.Video,
            Codec.Mp2 or Codec.Wmav1 or Codec.Wmav2 or Codec.NellyMoser or Codec.Adpcm_swf or Codec.Adpcm_Ms
                or Codec.Pcm_u8 or Codec.Pcm_s16be or Codec.Pcm_s24Le or Codec.Aac or Codec.Vorbis
                or Codec.Opus or Codec.Mp3 or Codec.Wmapro or Codec.Ac3 or Codec.Flac or Codec.Eac3
                or Codec.Dts => CodecCategory.Audio,
            Codec.Data => CodecCategory.Data,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
        };
    }
}
