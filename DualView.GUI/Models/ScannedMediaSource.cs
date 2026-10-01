using System;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;

namespace DualView.GUI.Models;

public sealed class ScannedMediaSource : ServerMediaSource
{
    private readonly long collectionId;

    public ScannedMediaSource(long collectionId, ScannedMediaDTO media, IServiceProvider serviceProvider)
        : base(CreateInfo(media), serviceProvider)
    {
        this.collectionId = collectionId;
    }

    private ScannedMediaSource(long collectionId, IConfiguredMediaInfo mediaInfo, IServiceProvider serviceProvider)
        : base(mediaInfo, serviceProvider)
    {
        this.collectionId = collectionId;
    }

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
        return new ScannedMediaSource(collectionId, Info, VideoPlayerServiceProvider);
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
