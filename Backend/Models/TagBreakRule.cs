using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

// TODO: check if this is actually used in the C++ code and if not, just remove this
[Index(nameof(TagString), IsUnique = true)]
public class TagBreakRule : UpdateableModel, ISoftDelete
{
    public TagBreakRule(string tagString, long actualTagId)
    {
        TagString = tagString;
        ActualTagId = actualTagId;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    [Required]
    public string TagString { get; set; }

    public long ActualTagId { get; set; }
    public Tag ActualTag { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public ICollection<TagModifier> Modifiers { get; set; } = new List<TagModifier>();
}
