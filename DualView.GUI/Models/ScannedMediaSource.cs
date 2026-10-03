using System;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;

namespace DualView.GUI.Models;

public sealed class ScannedMediaSource : ServerMediaSource
{
    private readonly long collectionId;

    public ScannedMediaSource(long collectionId, ScannedMediaDTO media, IServiceProvider serviceProvider)
        : this(collectionId, CreateInfo(media), string.Join(", ", media.Tags), serviceProvider)
    {
    }

    private ScannedMediaSource(long collectionId, IConfiguredMediaInfo mediaInfo, string tagsString,
        IServiceProvider serviceProvider)
        : base(mediaInfo, serviceProvider)
    {
        this.collectionId = collectionId;
        TagsString = tagsString;
    }

    public string TagsString { get; }

    public override string GetThumbnailDownloadUrl()
    {
        return $"api/v1/scannedCollection/{collectionId}/thumbnail/{ServerId}";
    }

    public override string GetFullDownloadUrl()
    {
        return $"api/v1/scannedCollection/{collectionId}/content/{ServerId}";
    }

    public override IVisualMediaSource Clone()
    {
        return new ScannedMediaSource(collectionId, Info, TagsString, VideoPlayerServiceProvider);
    }

    private static ConfiguredMediaInfo CreateInfo(ScannedMediaDTO media)
    {
        if (media.MediaType.HasValue)
            return new ConfiguredMediaInfo(media.Name, media.Id, media.Id, media.MediaType.Value, 0, 0);

        var mediaType = MediaType.Jpeg;
        try
        {
            mediaType = MediaTypeExtensions.TypeFromExtension(System.IO.Path.GetExtension(media.DownloadUrl));
        }
        catch (ArgumentException)
        {
        }

        return new ConfiguredMediaInfo(media.Name, media.Id, media.Id, mediaType, 0, 0);
    }
}
