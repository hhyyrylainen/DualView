namespace DualView.Shared.Models.DTO;

public class AppliedTagDTO
{
    public AppliedTagDTO(long id, long tagId)
    {
        Id = id;
        TagId = tagId;
    }

    public long Id { get; set; }
    public long TagId { get; set; }
    public TagDTO? Tag { get; set; }
    public List<TagModifierDTO> Modifiers { get; set; } = new List<TagModifierDTO>();
    public long? CombinedWithId { get; set; }
    public string? CombineWord { get; set; }
}
