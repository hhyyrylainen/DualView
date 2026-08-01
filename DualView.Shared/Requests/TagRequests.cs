using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.Enums;

namespace DualView.Shared.Requests;

public class CreateTagRequest
{
    [Required]
    public string Name { get; set; } = "";

    [Required]
    public TagCategory Category { get; set; }
}

public class UpdateTagRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public TagCategory? Category { get; set; }
    public long? ExampleMediaId { get; set; }
}

public class CreateTagAliasRequest
{
    [Required]
    public string Alias { get; set; } = "";
}

public class AddImplicationRequest
{
    [Required]
    public long ImpliedTagId { get; set; }
}
