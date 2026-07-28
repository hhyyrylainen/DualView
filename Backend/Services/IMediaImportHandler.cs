using Backend.Models;

namespace Backend.Services;

public interface IMediaImportHandler
{
    public Task<MediaFile> ImportMedia(string fileName, Stream stream, long targetCollectionId, bool markAsKeep,
        long? parentMediaId = null);
}
