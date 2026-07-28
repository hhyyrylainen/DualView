using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class RenameRequest(string name)
{
    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = name;
}
