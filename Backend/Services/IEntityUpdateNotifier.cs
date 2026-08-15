using DualView.Shared.Models;

namespace Backend.Services;

public interface IEntityUpdateNotifier
{
    public Task NotifyAppSettingsUpdated();

    public Task NotifyMediaFoldersUpdated();
    public Task NotifyMediaFolderContentsUpdated(long folderId);

    public Task NotifyMediaUpdated(long id);

    public Task OperationStatusUpdated(OperationStatusUpdate update);

    // DualView specific
    public Task NotifyCollectionUpdated(long id);
    public Task NotifyCollectionContentsUpdated(long id);
    public Task NotifyTagUpdated(long id);
    public Task NotifyTagsUpdated();
    public Task NotifyTagModifiersUpdated();
    public Task NotifyDownloadGalleriesUpdated();
    public Task NotifyDownloadGalleryUpdated(long id);
    public Task NotifyUploadSectionActiveChanged(long? sectionId);
    public Task NotifyUploadSectionsUpdated();
    public Task NotifyUploadSectionUpdated(long sectionId);
    public Task NotifyUploadSectionContentsUpdated(long sectionId);
}
