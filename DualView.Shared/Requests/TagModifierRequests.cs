using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class CreateModifierRequest
{
    [Required]
    public string Name { get; set; } = "";
}

public class UpdateModifierRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class CreateTagModifierAliasRequest
{
    [Required]
    public string Alias { get; set; } = "";
}
