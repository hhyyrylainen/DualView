using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   A collection of media
/// </summary>
[Index(nameof(NameLowerCase), IsUnique = true)]
public class Collection : UpdateableModel, IDTOProvider<CollectionDTO>, ISoftDelete
{
    public const long UncategorizedCollectionId = 1;

    public Collection(string name)
    {
        Name = name;
        NameLowerCase = name.ToLowerInvariant();
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
            NameLowerCase = value.ToLowerInvariant();
        }
    }

    [MaxLength(200)]
    public string NameLowerCase { get; set; }

    public long? PreviewMediaId { get; set; }

    public DateTime? LastViewed { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<MediaFolder> Folders { get; set; } = new List<MediaFolder>();

    public ICollection<CollectionItem> Items { get; set; } = new List<CollectionItem>();

    public ICollection<AppliedTag> AppliedTags { get; set; } = new List<AppliedTag>();

    public CollectionDTO GetDTO()
    {
        return new CollectionDTO(Name)
        {
            Id = Id,
            FolderIds = Folders.Select(f => f.Id).ToList()
        };
    }
}

[Index(nameof(CollectionId), nameof(SequenceNumber), IsUnique = true)]
public class CollectionItem
{
    public long CollectionId { get; set; }
    public Collection Collection { get; set; } = null!;

    public long MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; } = null!;

    public int SequenceNumber { get; set; }
}
