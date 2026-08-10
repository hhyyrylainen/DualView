using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Name), IsUnique = true)]
public class TagModifier : UpdateableModel, ISoftDelete, IDTOProvider<TagModifierDTO>
{
    public TagModifier(string name)
    {
        Name = name;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public bool IsDeleted { get; set; }

    public TagModifierDTO GetDTO()
    {
        return new TagModifierDTO(Name)
        {
            Id = Id,
            Description = Description,
        };
    }
}
