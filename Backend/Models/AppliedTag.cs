using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using Backend.Utilities;

namespace Backend.Models;

public class AppliedTag : IDTOProvider<AppliedTagDTO>
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
    public ICollection<UploadSection> UploadSections { get; set; } = new List<UploadSection>();
    public ICollection<ScannedCollection> ScannedCollections { get; set; } = new List<ScannedCollection>();
    public ICollection<FoundMedia> FoundMedia { get; set; } = new List<FoundMedia>();

    public AppliedTagDTO GetDTO()
    {
        return new AppliedTagDTO(Id, TagId)
        {
            Tag = Tag?.GetDTO(),
            Modifiers = Modifiers.Select(m => m.GetDTO()).ToList(),
            CombinedWithId = CombinedWithId,
            CombineWord = CombineWord,
        };
    }
}
