using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

/// <summary>
///   A collection of media
/// </summary>
// [Index(nameof(NameLowerCase), IsUnique = true)]
public class Collection : UpdateableModel, IDTOProvider<CollectionDTO>
{
    public Collection(string name, long folderId)
    {
        Name = name;
        FolderId = folderId;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; }

    // TODO: add an all lowercase name property and a unique index for it (should do the same for folders to make searching easier)

    public long FolderId { get; set; }
    public MediaFolder Folder { get; set; } = null!;

    public ICollection<CollectionItem> Items { get; set; } = new List<CollectionItem>();

    public CollectionDTO GetDTO()
    {
        return new CollectionDTO(Name)
        {
            Id = Id,
            FolderId = FolderId
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
