using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Models.DTO;

public class CollectionDTO(string name)
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = name;

    public List<long> FolderIds { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastViewed { get; set; }

    public int ImageGroupSize { get; set; } = 1;

    public bool IsDeleted { get; set; }
}
