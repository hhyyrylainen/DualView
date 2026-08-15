using System;
using System.Threading.Tasks;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;

namespace DualView.GUI.Models;

public sealed class ImportSectionBrowse(
    long sectionId,
    IClientDatabaseService databaseService,
    IServiceProvider serviceProvider) : ICollectionBrowse
{
    public async Task<(int Count, int? Index)> GetBrowseInfoAsync(long? mediaId)
    {
        var media = (await databaseService.GetUploadSectionAsync(sectionId))?.Media;
        if (media == null)
            return (0, null);

        var index = mediaId.HasValue ? media.FindIndex(item => item.Id == mediaId.Value) : -1;
        return (media.Count, index >= 0 ? index : null);
    }

    public async Task<IVisualMediaSource?> GetMediaAsync(int index)
    {
        var media = (await databaseService.GetUploadSectionAsync(sectionId))?.Media;
        if (media == null || index < 0 || index >= media.Count)
            return null;

        return new ServerMediaSource(new ConfiguredMediaInfo(media[index]), serviceProvider);
    }
}
