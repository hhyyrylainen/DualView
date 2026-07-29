using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Models.DTO;

public class CollectionDTO(string name)
{
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = name;

    public long FolderId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
