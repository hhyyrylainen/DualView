using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class TagDTO
{
    public TagDTO(string name)
    {
        Name = name;
    }

    public long Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public TagCategory Category { get; set; }
    public long? ExampleMediaId { get; set; }
}
