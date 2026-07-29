using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Services;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(ParentId), nameof(NameLowerCase), IsUnique = true)]
public class MediaFolder : UpdateableModel, IDTOProvider<MediaFolderDTO>,
    IInfoProvider<MediaFolderInfo>, ISoftDelete
{
    public const long UncategorizedFolderId = 1;

    public MediaFolder(string name, long? parentId)
    {
        Name = name;
        NameLowerCase = name.ToLowerInvariant();
        ParentId = parentId;
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

    public bool IsDeleted { get; set; }

    public long? ParentId { get; set; }

    public MediaFolder? Parent { get; set; }

    public ICollection<MediaFolder> SubFolders { get; set; } = new List<MediaFolder>();

    public ICollection<Collection> ContainedCollections { get; set; } = new List<Collection>();

    public static async Task<MediaFolder?> GetOrCreateAtPath(string path, IDatabaseService databaseService,
        bool allowCreating, bool allowRootPathCreation = false)
    {
        var pathElements = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathElements.Length == 0)
        {
            if (path == "/")
                return null;

            throw new ArgumentException("Empty path");
        }

        MediaFolder? current = null;
        bool root = true;
        foreach (var pathElement in pathElements)
        {
            var next = await databaseService.GetMediaFolderAsync(pathElement, current?.Id);

            if (next == null)
            {
                if (!allowCreating)
                {
                    // Invalid path
                    return null;
                }

                // Could not find the folder, but we can make it
                if (!allowRootPathCreation && root)
                {
                    // TODO: should this throw due to the settings?
                    return null;
                }

                next = await databaseService.CreateMediaFolderAsync(pathElement, current?.Id);
            }

            current = next ?? throw new Exception("Logic error in path handling");
            root = false;
        }

        return current;
    }

    public MediaFolderDTO GetDTO()
    {
        return new MediaFolderDTO(Name)
        {
            Id = Id,
            ParentId = ParentId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    public MediaFolderInfo GetInfo()
    {
        return new MediaFolderInfo(Name)
        {
            Id = Id,
            ParentId = ParentId,
        };
    }
}
