using System;
using System.Threading.Tasks;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;

namespace DualView.GUI.Models;

public sealed class CollectionBrowse(
    long collectionId,
    IClientDatabaseService databaseService,
    IServiceProvider serviceProvider) : ICollectionBrowse
{
    public async Task<(int Count, int? Index)> GetBrowseInfoAsync(long? mediaId)
    {
        var info = await databaseService.GetCollectionBrowseInfoAsync(collectionId, mediaId);
        return (info.Count, info.Index);
    }

    public async Task<IVisualMediaSource?> GetMediaAsync(int index)
    {
        var media = await databaseService.GetCollectionMediaAtIndexAsync(collectionId, index);
        if (media == null)
            return null;

        return new ServerMediaSource(new ConfiguredMediaInfo(media), serviceProvider);
    }
}
