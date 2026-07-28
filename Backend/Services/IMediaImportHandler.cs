using Backend.Models;

namespace Backend.Services;

public interface IMediaImportHandler
{
    public Task<ConfiguredMedia> ImportMedia(string fileName, Stream stream, string targetFolder, bool markAsKeep,
        long? derivedFromId = null);
}
