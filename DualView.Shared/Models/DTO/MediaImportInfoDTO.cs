namespace DualView.Shared.Models.DTO;

public class MediaImportInfoDTO
{
    public MediaImportInfoDTO(long mediaFileId)
    {
        MediaFileId = mediaFileId;
    }

    public long Id { get; set; }
    public long MediaFileId { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourcePath { get; set; }
    public string? Referrer { get; set; }

    public string? PreferredName { get; set; }
    public string? TagsString { get; set; }
    public long? DownloadGalleryId { get; set; }
}
