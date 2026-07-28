using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Models.DTO;

public class MediaFolderDTO(string name) : MediaFolderInfo(name)
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MediaFolderInfo(string name) : IFolderInfo
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = name;

    public long? ParentId { get; set; }
}
