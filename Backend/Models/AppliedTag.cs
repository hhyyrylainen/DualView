using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

public class AppliedTag
{
    public AppliedTag(long tagId)
    {
        TagId = tagId;
    }

    [Key]
    public long Id { get; set; }

    public long TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public long? CombinedWithId { get; set; }
    public AppliedTag? CombinedWith { get; set; }

    [MaxLength(50)]
    public string? CombineWord { get; set; }

    public ICollection<TagModifier> Modifiers { get; set; } = new List<TagModifier>();
    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
    public ICollection<Collection> Collections { get; set; } = new List<Collection>();
}
