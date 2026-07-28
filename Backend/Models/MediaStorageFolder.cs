using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Services;
using Backend.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Models;

[Index(nameof(ParentId), nameof(Name), IsUnique = true)]
public class MediaStorageFolder : UpdateableModel, IDTOProvider<MediaStorageFolderDTO>,
    IInfoProvider<MediaStorageFolderInfo>
{
    public const long UncategorizedFolderId = 1;

    public MediaStorageFolder(string name, long? parentId)
    {
        Name = name;
        ParentId = parentId;
    }

    [Key]
    public long Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; }

    public long? ParentId { get; set; }

    public MediaStorageFolder? Parent { get; set; }

    public ICollection<MediaStorageFolder> SubFolders { get; set; } = new List<MediaStorageFolder>();

    public ICollection<ConfiguredMedia> ContainedItems { get; set; } = new List<ConfiguredMedia>();

    public static async Task<MediaStorageFolder?> GetOrCreateAtPath(string path, IDatabaseService databaseService,
        bool allowCreating, bool allowRootPathCreation = false)
    {
        var pathElements = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        MediaStorageFolder? current = null;
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

        if (current == null)
            throw new ArgumentException("Empty path");

        return current;
    }

    public MediaStorageFolderDTO GetDTO()
    {
        return new MediaStorageFolderDTO(Name)
        {
            Id = Id,
            ParentId = ParentId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    public MediaStorageFolderInfo GetInfo()
    {
        return new MediaStorageFolderInfo(Name)
        {
            Id = Id,
            ParentId = ParentId,
        };
    }
}
