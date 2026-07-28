namespace DualView.Shared.Models.DTO;

public class TagModifierDTO
{
    public TagModifierDTO(string name)
    {
        Name = name;
    }

    public long Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
}
