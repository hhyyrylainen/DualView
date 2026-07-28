using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Name), IsUnique = true)]
public class TagAlias
{
    public TagAlias(string name, long tagId)
    {
        Name = name;
        TagId = tagId;
    }

    [Key]
    [MaxLength(200)]
    public string Name { get; set; }

    public long TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
