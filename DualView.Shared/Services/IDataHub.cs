using DualView.Shared.Models;

namespace DualView.Shared.Services;

public interface IDataHub
{
    public Task AppSettingsUpdated();

    public Task MediaFoldersUpdated();
    public Task MediaFolderContentsUpdated(long folderId);

    public Task MediaUpdated(long id);

    public Task OperationStatusUpdated(OperationStatusUpdate update);

    // DualView specific
    public Task CollectionUpdated(long id);
    public Task CollectionContentsUpdated(long id);
    public Task TagUpdated(long id);
    public Task TagsUpdated();
    public Task TagModifiersUpdated();
    public Task DownloadGalleriesUpdated();
    public Task DownloadGalleryUpdated(long id);
    public Task UploadSectionActiveChanged(long? sectionId);
    public Task UploadSectionsUpdated();
    public Task UploadSectionUpdated(long sectionId);
    public Task UploadSectionContentsUpdated(long sectionId);
    public Task MissingTagsUpdated();
}
