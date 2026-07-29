using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(Alias), IsUnique = true)]
public class TagSuperAlias
{
    public TagSuperAlias(string alias, string expanded)
    {
        Alias = alias;
        Expanded = expanded;
    }

    [Key]
    [MaxLength(200)]
    public string Alias { get; set; }

    [Required]
    [MaxLength(500)]
    public string Expanded { get; set; }
}
