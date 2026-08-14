using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(NameLowercase))]
[Index(nameof(DisplayIndex), IsUnique = true)]
public class UploadSection : UpdateableModel
{
    public UploadSection(string name)
    {
        Name = name;
        NameLowercase = name.ToLowerInvariant();
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string Name
    {
        get;
        set
        {
            field = value;
            NameLowercase = value.ToLowerInvariant();
        }
    }

    [MaxLength(200)]
    public string NameLowercase { get; set; }

    /// <summary>
    ///   If true, this section isn't destroyed when it becomes empty
    /// </summary>
    public bool KeepTarget { get; set; }

    /// <summary>
    ///   Ordering of the display sections
    /// </summary>
    public int DisplayIndex { get; set; }

    public DateTime? LastImported { get; set; }

    /// <summary>
    ///   Only one section can be active as the default import target
    /// </summary>
    public bool Selected { get; set; }

    /// <summary>
    ///   The folder in which the target collection should be created.
    /// </summary>
    public long TargetFolderId { get; set; } = MediaFolder.RootFolderId;

    /// <summary>
    ///   Removes imported media from this section after a successful import.
    /// </summary>
    public bool RemoveAfterImport { get; set; } = true;

    public ICollection<UploadSectionItem> Items { get; set; } = new List<UploadSectionItem>();
}

[Index(nameof(UploadSectionId), nameof(Index), IsUnique = true)]
public class UploadSectionItem
{
    public long UploadSectionId { get; set; }
    public UploadSection UploadSection { get; set; } = null!;

    public long MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; } = null!;

    public int Index { get; set; }
}
