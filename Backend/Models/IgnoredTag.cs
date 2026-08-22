using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Name), IsUnique = true)]
public class IgnoredTag
{
    public IgnoredTag(string name)
    {
        Name = name.Trim().ToLowerInvariant();
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(4096)]
    public string Name { get; set; }
}
