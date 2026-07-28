using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Models.DTO;

public class MediaStorageFolderDTO(string name) : MediaStorageFolderInfo(name)
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MediaStorageFolderInfo(string name) : IFolderInfo
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = name;

    public long? ParentId { get; set; }
}
