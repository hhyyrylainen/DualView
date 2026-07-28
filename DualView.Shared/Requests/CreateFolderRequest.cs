using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class CreateFolderRequest(string name)
{
    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = name;

    public long? ParentFolderId { get; set; }
}
