namespace DualView.Shared.Models.DTO;

public class UploadSectionDTO
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool KeepTarget { get; set; }
    public bool Selected { get; set; }
    public bool RemoveAfterImport { get; set; }
    public long TargetFolderId { get; set; }
    public List<MediaFileDTO> Media { get; set; } = new();
}
