using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using Backend.Services;
using Backend.Utilities;

namespace Backend.Models;

public class MediaFolder : UpdateableModel, IDTOProvider<MediaFolderDTO>,
    IInfoProvider<MediaFolderInfo>, ISoftDelete
{
    public const long RootFolderId = MediaFolderInfo.RootFolderId;

    public MediaFolder(string name)
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

    public bool IsDeleted { get; set; }

    public ICollection<MediaFolder> Parents { get; set; } = new List<MediaFolder>();

    public ICollection<MediaFolder> SubFolders { get; set; } = new List<MediaFolder>();

    public ICollection<Collection> ContainedCollections { get; set; } = new List<Collection>();

    public static async Task<MediaFolder?> GetOrCreateAtPath(string path, IDatabaseService databaseService,
        bool allowCreating, bool allowRootPathCreation = false)
    {
        var rootFolder = await databaseService.GetMediaFolderAsync(RootFolderId);
        if (rootFolder == null)
            throw new Exception("Root folder not found");

        var pathElements = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathElements.Length == 0)
        {
            if (path == "/" || string.IsNullOrWhiteSpace(path))
                return rootFolder;

            throw new ArgumentException("Empty path");
        }

        MediaFolder current = rootFolder;
        int startIndex = 0;

        // If the first element is the name of the root folder, we skip it
        if (pathElements[0].Equals(rootFolder.Name, StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        for (int i = startIndex; i < pathElements.Length; ++i)
        {
            var pathElement = pathElements[i];
            var next = await databaseService.GetMediaFolderAsync(pathElement, current.Id);

            if (next == null)
            {
                if (!allowCreating)
                {
                    // Invalid path
                    return null;
                }

                // Could not find the folder, but we can make it
                if (!allowRootPathCreation && i == startIndex && current.Id == RootFolderId)
                {
                    // For now keeping this logic, it means creating a folder directly in root is not allowed
                    // unless allowRootPathCreation is true.
                    return null;
                }

                next = await databaseService.CreateMediaFolderAsync(pathElement, current.Id);
            }

            current = next ?? throw new Exception("Logic error in path handling");
        }

        return current;
    }

    public MediaFolderDTO GetDTO()
    {
        return new MediaFolderDTO(Name)
        {
            Id = Id,
            ParentIds = Parents.Select(p => p.Id).ToList(),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    public MediaFolderInfo GetInfo()
    {
        return new MediaFolderInfo(Name)
        {
            Id = Id,
            ParentIds = Parents.Select(p => p.Id).ToList(),
        };
    }
}
