using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class CreateTagSuperAliasRequest
{
    [Required]
    public string Alias { get; set; } = "";

    [Required]
    public string Expanded { get; set; } = "";
}

public class UpdateTagSuperAliasRequest
{
    [Required]
    public string Alias { get; set; } = "";

    [Required]
    public string Expanded { get; set; } = "";
}
