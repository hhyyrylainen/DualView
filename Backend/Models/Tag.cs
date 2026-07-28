using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Name), IsUnique = true)]
public class Tag : UpdateableModel, ISoftDelete
{
    public Tag(string name, TagCategory category)
    {
        Name = name;
        Category = category;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public TagCategory Category { get; set; }

    public long? ExampleMediaId { get; set; }
    public MediaFile? ExampleMedia { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<TagAlias> Aliases { get; set; } = new List<TagAlias>();
    public ICollection<TagImply> Implies { get; set; } = new List<TagImply>();
    public ICollection<TagImply> ImpliedBy { get; set; } = new List<TagImply>();
}
