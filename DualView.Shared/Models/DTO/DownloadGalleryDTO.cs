namespace DualView.Shared.Models.DTO;

public class DownloadGalleryDTO
{
    public DownloadGalleryDTO(string galleryUrl)
    {
        GalleryUrl = galleryUrl;
    }

    public long Id { get; set; }
    public string GalleryUrl { get; set; }
    public string? TargetPath { get; set; }
    public string? GalleryName { get; set; }
    public string? CurrentlyScannedUrl { get; set; }
    public bool IsDownloaded { get; set; }
    public string? TagsString { get; set; }
    public bool IsDeleted { get; set; }
}
