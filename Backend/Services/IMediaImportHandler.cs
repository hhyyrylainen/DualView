using Backend.Models;

namespace Backend.Services;

public interface IMediaImportHandler
{
    public Task<MediaFile> ImportMedia(string fileName, Stream stream, string? sectionName, string? sourcePath = null);

    // TODO: create a direct server-used import that goes straight to a collection rather than the import window
    /*
     *     public Task<MediaFile> ImportMedia(string fileName, Stream stream, long targetCollectionId,
     *           long? parentMediaId = null);
     */
}
