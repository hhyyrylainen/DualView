using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class CreateMediaConfigRequest(string name, long parentMediaId)
{
    [MaxLength(200)]
    public string Name { get; set; } = name;

    public long ParentMediaId { get; set; } = parentMediaId;

    [MaxLength(20)]
    public List<string>? FoldersToAdd { get; set; }

    /// <summary>
    ///   If true, creates the (root) folders specified in FoldersToAdd if they are missing.
    ///   Note that this isn't currently really made to prevent the creation of stuff if they aren't root items.
    /// </summary>
    public bool CreateFolders { get; set; }

    /// <summary>
    ///   If set to false, won't mark the created configs as to be kept automatically and not cleaned
    /// </summary>
    public bool MarkAsKeep { get; set; } = true;
}
