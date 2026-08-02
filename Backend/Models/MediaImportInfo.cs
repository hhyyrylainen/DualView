using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;

namespace Backend.Models;

public class MediaImportInfo : UpdateableModel, IDTOProvider<MediaImportInfoDTO>
{
    public MediaImportInfo(long mediaFileId, DateTime importDate)
    {
        MediaFileId = mediaFileId;
        ImportDate = importDate;
    }

    [Key]
    public long Id { get; set; }

    public long MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; } = null!;

    [MaxLength(1024)]
    public string? SourceUrl { get; set; }

    [MaxLength(1024)]
    public string? SourcePath { get; set; }

    public DateTime ImportDate { get; set; }

    [MaxLength(1024)]
    public string? Referrer { get; set; }

    [MaxLength(1024)]
    public string? PreferredName { get; set; }

    [MaxLength(4096)]
    public string? TagsString { get; set; }

    public long? DownloadGalleryId { get; set; }
    public DownloadGallery? DownloadGallery { get; set; }

    public MediaImportInfoDTO GetDTO()
    {
        return new MediaImportInfoDTO(MediaFileId, ImportDate)
        {
            Id = Id,
            SourceUrl = SourceUrl,
            SourcePath = SourcePath,
            Referrer = Referrer,
            PreferredName = PreferredName,
            TagsString = TagsString,
            DownloadGalleryId = DownloadGalleryId,
        };
    }
}
