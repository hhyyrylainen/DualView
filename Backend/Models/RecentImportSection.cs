using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(NameLowercase), IsUnique = true)]
public class RecentImportSection
{
    public RecentImportSection(string name)
    {
        Name = name;
        NameLowercase = name.ToLowerInvariant();
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string Name
    {
        get;
        set
        {
            field = value;
            NameLowercase = value.ToLowerInvariant();
        }
    }

    [MaxLength(200)]
    public string NameLowercase { get; set; }

    public DateTime LastUsed { get; set; }
}
