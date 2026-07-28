using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Name), IsUnique = true)]
public class TagModifierAlias
{
    public TagModifierAlias(string name, long modifierId)
    {
        Name = name;
        ModifierId = modifierId;
    }

    [Key]
    [MaxLength(200)]
    public string Name { get; set; }

    public long ModifierId { get; set; }
    public TagModifier Modifier { get; set; } = null!;
}
