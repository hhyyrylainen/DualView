namespace DualView.Shared.Models.Enums;

public enum MediaType
{
    Png,
    Jpeg,
    Gif,
    Webp,
    WebpAnimated,
    Mp4,
    Mkv,
    Webm,
    Bmp,
    Tif,
}

public static class MediaTypeExtensions
{
    public static bool IsImage(this MediaType type)
    {
        switch (type)
        {
            case MediaType.Png:
            case MediaType.Jpeg:
            case MediaType.Gif:
            case MediaType.Webp:
            case MediaType.WebpAnimated:
            case MediaType.Bmp:
            case MediaType.Tif:
                return true;
        }

        return false;
    }

    public static bool IsAnimated(this MediaType type)
    {
        switch (type)
        {
            case MediaType.Gif:
            case MediaType.WebpAnimated:
                return true;

            // Videos are also technically animated
            case MediaType.Mp4:
            case MediaType.Mkv:
            case MediaType.Webm:
                return true;
        }

        return false;
    }

    public static MediaType MakeAnimated(this MediaType type)
    {
        if (type == MediaType.Webp || type == MediaType.WebpAnimated)
            return MediaType.WebpAnimated;

        // If already animated, return itself
        if (type == MediaType.Gif)
            return MediaType.Gif;

        throw new Exception($"Cannot make animated from: {type}");
    }

    public static MediaType TypeFromExtension(string extension)
    {
        if (extension == ".png")
            return MediaType.Png;

        if (extension == ".jpeg" || extension == ".jpg" || extension == ".jfif" || extension == ".heic")
            return MediaType.Jpeg;

        if (extension == ".gif")
            return MediaType.Gif;

        if (extension == ".webp")
            return MediaType.Webp;

        if (extension == ".mp4")
            return MediaType.Mp4;

        if (extension == ".mkv")
            return MediaType.Mkv;

        if (extension == ".webm")
            return MediaType.Webm;

        if (extension == ".bmp")
            return MediaType.Bmp;

        if (extension == ".tif")
            return MediaType.Tif;

        throw new ArgumentException("Unknown file extension: " + extension);
    }

    public static string ExtensionFromType(this MediaType type)
    {
        switch (type)
        {
            case MediaType.Png:
                return ".png";
            case MediaType.Jpeg:
                return ".jpg";
            case MediaType.Gif:
                return ".gif";
            case MediaType.Webp:
            case MediaType.WebpAnimated:
                return ".webp";
            case MediaType.Mp4:
                return ".mp4";
            case MediaType.Mkv:
                return ".mkv";
            case MediaType.Webm:
                return ".webm";
            case MediaType.Bmp:
                return ".bmp";
            case MediaType.Tif:
                return ".tif";
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public static string ToMimeType(this MediaType type)
    {
        switch (type)
        {
            case MediaType.Png:
                return "image/png";
            case MediaType.Jpeg:
                return "image/jpeg";
            case MediaType.Gif:
                return "image/gif";
            case MediaType.Webp:
            case MediaType.WebpAnimated:
                return "image/webp";
            case MediaType.Mp4:
                return "video/mp4";
            case MediaType.Mkv:
                return "video/x-matroska";
            case MediaType.Webm:
                return "video/webm";
            case MediaType.Bmp:
                return "image/bmp";
            case MediaType.Tif:
                return "image/tiff";
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}
