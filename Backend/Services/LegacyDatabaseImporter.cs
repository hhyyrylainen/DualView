using System.Globalization;
using System.Text;
using Backend.Database;
using Backend.Models;
using Backend.Utilities;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using DualView.Shared.Utils;
using ImageMagick;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Imports the last C++ DualView database. The old numeric IDs are deliberately never
///   copied to the new database: old IDs are only used as temporary relationship maps.
/// </summary>
public class LegacyDatabaseImporter : ILegacyDatabaseImporter
{
    /// <summary>
    ///   This is how many images are imported in a single batch. This makes the import process much faster as DB
    ///   flushes don't happen for each imported image.
    /// </summary>
    private const int BulkImportImages = 100;

    private readonly ILogger<LegacyDatabaseImporter> logger;
    private readonly AppDbContext dbContext;
    private readonly IDataFolderService dataFolderService;

    /// <summary>
    ///   When true, tries to fix some common corruptions by editing the legacy database.
    /// </summary>
    private readonly bool tryFixOldCollection = false;

    /// <summary>
    ///   If set to true, skips importing files that are broken. This is useful to get a mostly working import if there
    ///   are many bad files.
    /// </summary>
    private readonly bool skipImportBrokenFiles = false;

    /// <summary>
    ///   These files are corrupted but allowed to be imported with the changed hash. NOTE: clear this array before
    ///   committing!
    /// </summary>
    private readonly string[] allowedHashChangeFiles =
    [
    ];

    /// <summary>
    ///   Similar to <see cref="allowedHashChangeFiles"/> but allows entire folders to be ignored.
    /// </summary>
    private readonly string[] allowedHashChangeFolderPrefixes =
    [
    ];

    /// <summary>
    ///   These files will be ignored during import. Can be used to ignore media that has no replacements available.
    ///   NOTE: clear this list before committing!
    /// </summary>
    private readonly HashSet<string> skipMediaImportHashes = new([
    ]);

    private DualViewSettings? settings;

    private int importedTotalImages;

    public LegacyDatabaseImporter(ILogger<LegacyDatabaseImporter> logger, AppDbContext dbContext,
        IDataFolderService dataFolderService)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.dataFolderService = dataFolderService;
    }

    public async Task ImportAsync(string databasePath, string legacyRootCollectionPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("The configured legacy DualView database was not found", databasePath);

        settings = await dbContext.AppSettings.FindAsync([1], cancellationToken) ??
                   throw new Exception("Settings not found");

        logger.LogInformation("Importing legacy DualView database from {Path}", databasePath);

        if (tryFixOldCollection)
        {
            var connectionStringWritable = new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(databasePath),
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
            }.ToString();

            await using var connectionWritable = new SqliteConnection(connectionStringWritable);
            await connectionWritable.OpenAsync(cancellationToken);

            await FixMediaIncorrectEncodings(connectionWritable, legacyRootCollectionPath, cancellationToken);
            await FixMediaMissingExtensions(connectionWritable, legacyRootCollectionPath, cancellationToken);
            await FixMediaBadExtensions(connectionWritable, legacyRootCollectionPath, cancellationToken);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tags = await ImportTagsAsync(connection, cancellationToken);
        var tagModifiers = await ImportTagModifiersAsync(connection, cancellationToken);
        var appliedTags = await ImportAppliedTagsAsync(connection, tags, tagModifiers, cancellationToken);
        var folders = await ImportFoldersAsync(connection, cancellationToken);
        var collections = await ImportCollectionsAsync(connection, tags, folders, cancellationToken);
        await ImportMediaAsync(connection, appliedTags, collections,
            legacyRootCollectionPath, cancellationToken);

        logger.LogInformation("Legacy DualView database import completed");
    }

    private async Task<Dictionary<long, long>> ImportTagsAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var tagIds = new Dictionary<long, long>();
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, name, category, description, is_private, deleted FROM tags ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var name = RequiredText(row, "name");
            var tag = await dbContext.Tags.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);
            if (tag == null)
            {
                tag = new Tag(name.ToLowerInvariant(), (TagCategory)row.GetInt32("category"))
                {
                    Description = row.GetText("description"),
                };
                dbContext.Tags.Add(tag);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            tagIds[row.GetInt64("id")] = tag.Id;
        }

        await ImportTagAliasesAsync(connection, tagIds, cancellationToken);
        await ImportTagRelationsAsync(connection, tagIds, cancellationToken);
        return tagIds;
    }

    private async Task ImportTagAliasesAsync(SqliteConnection connection, Dictionary<long, long> tagIds,
        CancellationToken cancellationToken)
    {
        await foreach (var row in ReadRowsAsync(connection, "SELECT name, meant_tag FROM tag_aliases",
                           cancellationToken))
        {
            if (!tagIds.TryGetValue(row.GetInt64("meant_tag"), out var tagId))
                continue;

            var alias = RequiredText(row, "name");
            if (!await dbContext.TagAliases.AnyAsync(item => item.Name.ToLower() == alias.ToLower(), cancellationToken))
                dbContext.TagAliases.Add(new TagAlias(alias.ToLowerInvariant(), tagId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ImportTagRelationsAsync(SqliteConnection connection, Dictionary<long, long> tagIds,
        CancellationToken cancellationToken)
    {
        await foreach (var row in ReadRowsAsync(connection, "SELECT primary_tag, to_apply FROM tag_implies",
                           cancellationToken))
        {
            long primary;
            long applied;
            try
            {
                if (!tagIds.TryGetValue(row.GetInt64("primary_tag"), out primary) ||
                    !tagIds.TryGetValue(row.GetInt64("to_apply"), out applied))
                {
                    continue;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Invalid tag imply from old data, ignoring: {Data}", row);
                continue;
            }

            if (!await dbContext.TagImplies.AnyAsync(
                    item => item.PrimaryTagId == primary && item.ToApplyTagId == applied,
                    cancellationToken))
            {
                dbContext.TagImplies.Add(new TagImply(primary, applied));
            }
        }

        await foreach (var row in ReadRowsAsync(connection, "SELECT alias, expanded FROM tag_super_aliases",
                           cancellationToken))
        {
            var alias = RequiredText(row, "alias");
            if (!await dbContext.TagSuperAliases.AnyAsync(item => item.Alias.ToLower() == alias.ToLower(),
                    cancellationToken))
            {
                dbContext.TagSuperAliases.Add(new TagSuperAlias(alias, RequiredText(row, "expanded")));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<long, long>> ImportFoldersAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var folderIds = new Dictionary<long, long> { [1] = MediaFolder.RootFolderId };
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, name, deleted FROM virtual_folders WHERE id <> 1 ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var name = SanitizeName(RequiredText(row, "name"));

            // Note some folders might have duplicate names as long as they were in different parts, so this isn't an
            // exact import!
            var folder = await dbContext.MediaFolders.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);
            if (folder == null)
            {
                folder = new MediaFolder(name);
                dbContext.MediaFolders.Add(folder);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            folderIds[row.GetInt64("id")] = folder.Id;
        }

        await foreach (var row in ReadRowsAsync(connection, "SELECT parent, child FROM folder_folder",
                           cancellationToken))
        {
            if (!folderIds.TryGetValue(row.GetInt64("parent"), out var parent) ||
                !folderIds.TryGetValue(row.GetInt64("child"), out var child) || parent == child)
            {
                continue;
            }

            var folder = await dbContext.MediaFolders.Include(item => item.Parents).FirstAsync(item => item.Id == child,
                cancellationToken);
            if (folder.Parents.All(item => item.Id != parent))
            {
                folder.Parents.Add(await dbContext.MediaFolders.FindAsync([parent], cancellationToken) ??
                                   throw new InvalidOperationException());
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return folderIds;
    }

    private async Task<Dictionary<long, long>> ImportTagModifiersAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var modifierIds = new Dictionary<long, long>();
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, name, description, deleted FROM tag_modifiers ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var name = RequiredText(row, "name").ToLowerInvariant();
            var modifier = await dbContext.TagModifiers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Name.ToLower() == name, cancellationToken);
            if (modifier == null)
            {
                modifier = new TagModifier(name.ToLowerInvariant())
                {
                    Description = row.GetText("description"),
                };
                dbContext.TagModifiers.Add(modifier);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            modifierIds[row.GetInt64("id")] = modifier.Id;
        }

        // Not actually used, so not ported to DualView 3
        /*await foreach (var row in ReadRowsAsync(connection,
                           "SELECT name, meant_modifier FROM tag_modifier_aliases",
                           cancellationToken))
        {
            if (!modifierIds.TryGetValue(row.GetInt64("meant_modifier"), out var modifierId))
                continue;

            var alias = RequiredText(row, "name").ToLowerInvariant();
            if (!await dbContext.TagModifierAliases.AnyAsync(item => item.Name.ToLower() == alias,
                    cancellationToken))
            {
                dbContext.TagModifierAliases.Add(new TagModifierAlias(alias.ToLowerInvariant(), modifierId));
            }
        }*/

        await dbContext.SaveChangesAsync(cancellationToken);
        return modifierIds;
    }

    private async Task<Dictionary<long, long>> ImportAppliedTagsAsync(SqliteConnection connection,
        Dictionary<long, long> tagIds, Dictionary<long, long> modifierIds, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<long, LegacyAppliedTag>();
        await foreach (var row in ReadRowsAsync(connection, "SELECT id, tag FROM applied_tag ORDER BY id",
                           cancellationToken))
        {
            definitions[row.GetInt64("id")] = new LegacyAppliedTag(row.GetInt64("tag"));
        }

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT to_tag, modifier FROM applied_tag_modifier ORDER BY to_tag, modifier",
                           cancellationToken))
        {
            if (definitions.TryGetValue(row.GetInt64("to_tag"), out var definition))
                definition.ModifierIds.Add(row.GetInt64("modifier"));
        }

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT tag_left, tag_right, combined_with FROM applied_tag_combine ORDER BY tag_left",
                           cancellationToken))
        {
            if (definitions.TryGetValue(row.GetInt64("tag_left"), out var definition))
            {
                definition.CombinedWithId = row.GetInt64("tag_right");
                definition.CombineWord = RequiredText(row, "combined_with");
            }
        }

        var appliedTagIds = new Dictionary<long, long>();
        var currentlyImporting = new HashSet<long>();
        foreach (var legacyAppliedTagId in definitions.Keys.Order())
        {
            await ImportAppliedTagAsync(legacyAppliedTagId, definitions, tagIds, modifierIds, appliedTagIds,
                currentlyImporting, cancellationToken);
        }

        return appliedTagIds;
    }

    private async Task<long?> ImportAppliedTagAsync(long legacyAppliedTagId,
        Dictionary<long, LegacyAppliedTag> definitions, Dictionary<long, long> tagIds,
        Dictionary<long, long> modifierIds, Dictionary<long, long> appliedTagIds,
        HashSet<long> currentlyImporting, CancellationToken cancellationToken)
    {
        if (appliedTagIds.TryGetValue(legacyAppliedTagId, out var importedId))
            return importedId;

        if (!definitions.TryGetValue(legacyAppliedTagId, out var definition))
            throw new InvalidDataException($"Legacy applied tag {legacyAppliedTagId} does not exist");

        if (!currentlyImporting.Add(legacyAppliedTagId))
            throw new InvalidDataException($"Legacy applied tag combine contains a cycle at {legacyAppliedTagId}");

        try
        {
            if (!tagIds.TryGetValue(definition.TagId, out var tagId))
            {
                logger.LogWarning("Skipping legacy applied tag {AppliedTagId}: tag {TagId} was not imported",
                    legacyAppliedTagId, definition.TagId);
                return null;
            }

            var importedModifierIds = new List<long>();
            foreach (var legacyModifierId in definition.ModifierIds.Distinct())
            {
                if (!modifierIds.TryGetValue(legacyModifierId, out var modifierId))
                {
                    logger.LogWarning(
                        "Skipping legacy applied tag {AppliedTagId}: modifier {ModifierId} was not imported",
                        legacyAppliedTagId, legacyModifierId);
                    return null;
                }

                importedModifierIds.Add(modifierId);
            }

            long? combinedWithId = null;
            if (definition.CombinedWithId != null)
            {
                if (string.IsNullOrWhiteSpace(definition.CombineWord))
                {
                    throw new InvalidDataException(
                        $"Legacy applied tag {legacyAppliedTagId} has a combined tag without a combine word");
                }

                combinedWithId = await ImportAppliedTagAsync(definition.CombinedWithId.Value, definitions, tagIds,
                    modifierIds, appliedTagIds, currentlyImporting, cancellationToken);
                if (combinedWithId == null)
                {
                    logger.LogWarning(
                        "Skipping legacy applied tag {AppliedTagId}: its combined tag {CombinedTagId} was not imported",
                        legacyAppliedTagId, definition.CombinedWithId.Value);
                    return null;
                }
            }

            var candidates = await dbContext.AppliedTags.Include(item => item.Modifiers)
                .Where(item => item.TagId == tagId && item.CombinedWithId == combinedWithId &&
                               item.CombineWord == definition.CombineWord)
                .ToListAsync(cancellationToken);
            var appliedTag = candidates.FirstOrDefault(item =>
                item.Modifiers.Select(modifier => modifier.Id).Order().SequenceEqual(importedModifierIds.Order()));

            if (appliedTag == null)
            {
                appliedTag = new AppliedTag(tagId)
                {
                    CombinedWithId = combinedWithId,
                    CombineWord = definition.CombineWord,
                };

                var modifiers = await dbContext.TagModifiers
                    .Where(item => importedModifierIds.Contains(item.Id))
                    .ToListAsync(cancellationToken);
                foreach (var modifier in modifiers)
                    appliedTag.Modifiers.Add(modifier);

                dbContext.AppliedTags.Add(appliedTag);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            appliedTagIds[legacyAppliedTagId] = appliedTag.Id;
            return appliedTag.Id;
        }
        finally
        {
            currentlyImporting.Remove(legacyAppliedTagId);
        }
    }

    private async Task<Dictionary<long, long>> ImportCollectionsAsync(SqliteConnection connection,
        Dictionary<long, long> tagIds, Dictionary<long, long> folderIds, CancellationToken cancellationToken)
    {
        var collectionIds = new Dictionary<long, long>();
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, name, add_date, modify_date, last_view, deleted FROM collections ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var name = SanitizeName(RequiredText(row, "name"));
            var collection = await dbContext.Collections.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.NameLowerCase == name.ToLower(), cancellationToken);
            if (collection == null)
            {
                collection = new Collection(name)
                {
                    CreatedAt = ParseDate(row.GetText("add_date")),
                    UpdatedAt = ParseDate(row.GetText("modify_date")),
                };
                dbContext.Collections.Add(collection);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            collectionIds[row.GetInt64("id")] = collection.Id;
        }

        await foreach (var row in ReadRowsAsync(connection, "SELECT parent, child FROM folder_collection",
                           cancellationToken))
        {
            if (!folderIds.TryGetValue(row.GetInt64("parent"), out var folderId) ||
                !collectionIds.TryGetValue(row.GetInt64("child"), out var collectionId))
            {
                continue;
            }

            var collection = await dbContext.Collections.Include(item => item.Folders)
                .FirstAsync(item => item.Id == collectionId, cancellationToken);
            if (collection.Folders.All(item => item.Id != folderId))
            {
                collection.Folders.Add(await dbContext.MediaFolders.FindAsync([folderId], cancellationToken) ??
                                       throw new InvalidOperationException());
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return collectionIds;
    }

    private async Task FixMediaIncorrectEncodings(SqliteConnection connection, string legacyDirectory,
        CancellationToken cancellationToken)
    {
        var toProcess = new List<(long Id, string RelativePath, string Path, string Hash)>();

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, relative_path, width, height, name, extension, add_date, last_view, " +
                           "is_private, from_file, file_hash, deleted FROM pictures " +
                           "WHERE relative_path LIKE '%.png.jpg.jpg' ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var hash = RequiredText(row, "file_hash");
            var relativePath = RequiredText(row, "relative_path");
            var sourcePath = ResolveLegacyPath(relativePath, legacyDirectory);

            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Cannot fix non-existent path: {Path}", sourcePath);
                continue;
            }

            if (await IsValidMediaAsync(sourcePath, hash, cancellationToken))
                continue;

            toProcess.Add((row.GetInt64("id"), relativePath, sourcePath, hash));
        }

        foreach (var (id, relativePath, sourcePath, hash) in toProcess)
        {
            logger.LogInformation("Attempting repair on potentially wrong format file: {Path}", sourcePath);

            var targetPath = sourcePath.Replace(".png.jpg.jpg", ".png");
            var newRelativePath = relativePath.Replace(".png.jpg.jpg", ".png");
            File.Copy(sourcePath, targetPath);

            // If this can now load as PNG, update the DB to acknowledge the actual format of the file
            if (await IsValidMediaAsync(targetPath, hash, cancellationToken))
            {
                logger.LogInformation("File at path was actually a PNG: {Path}", sourcePath);

                await using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE pictures SET relative_path = @newRelativePath, extension = '.png' WHERE id = @id";
                command.Parameters.AddWithValue("newRelativePath", newRelativePath);
                command.Parameters.AddWithValue("id", id);
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);

                if (affected != 1)
                {
                    File.Delete(targetPath);
                    throw new InvalidOperationException(
                        $"Expected to update exactly one row, but updated {affected} rows");
                }

                File.Delete(sourcePath);
                logger.LogInformation("File at path was actually a PNG, and should now be fixed: {Path}", sourcePath);
            }
            else
            {
                logger.LogWarning("Could not fix file: {Path}", sourcePath);
                File.Delete(targetPath);
            }
        }
    }

    private async Task FixMediaMissingExtensions(SqliteConnection connection, string legacyDirectory,
        CancellationToken cancellationToken)
    {
        var toProcess = new List<(long Id, string RelativePath, string Path, string Name0)>();

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, relative_path, width, height, name, extension, add_date, last_view, " +
                           "is_private, from_file, file_hash, deleted FROM pictures " +
                           "WHERE extension ='' ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var relativePath = RequiredText(row, "relative_path");
            var extension = row.GetText("extension");
            var name = RequiredText(row, "name");

            if (!string.IsNullOrEmpty(extension) || Path.GetExtension(relativePath) != "")
                throw new Exception($"Extension is not empty: {extension}");

            var sourcePath = ResolveLegacyPath(relativePath, legacyDirectory);

            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Cannot fix non-existent path: {Path}", sourcePath);
                continue;
            }

            toProcess.Add((row.GetInt64("id"), relativePath, sourcePath, name));
        }

        foreach (var (id, relativePath, sourcePath, name) in toProcess)
        {
            logger.LogInformation("Attempting repair on missing extension: {Path}", sourcePath);

            string? targetExtension;
            MagickFormat format;
            try
            {
                var imageInfo = new MagickImageInfo(sourcePath);
                targetExtension = FileProbe.GetExtensionForFormat(imageInfo.Format);
                format = imageInfo.Format;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Cannot determine the format of file at: {Path}", sourcePath);
                continue;
            }

            if (targetExtension == null)
                throw new Exception("Could not determine extension for file: " + sourcePath);

            var finalPath = sourcePath + targetExtension;

            if (File.Exists(finalPath))
            {
                // Sadly, this occurs sometimes, so we can't fix those
                logger.LogError("File already exists at path (can't rename a file without extension to it): {Path}",
                    finalPath);
                continue;
            }

            var newRelativePath = relativePath + targetExtension;

            logger.LogInformation("File at path was actually a {Format} file: {Path}", format, sourcePath);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE pictures SET relative_path = @newRelativePath, extension = @newExtension, name = @newName " +
                "WHERE id = @id";
            command.Parameters.AddWithValue("newRelativePath", newRelativePath);
            command.Parameters.AddWithValue("newExtension", targetExtension);
            command.Parameters.AddWithValue("newName", name + targetExtension);
            command.Parameters.AddWithValue("id", id);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Expected to update exactly one row, but updated {affected} rows");
            }

            File.Move(sourcePath, finalPath);
            logger.LogInformation("File without extension was fixed and is now at path: {Path}", finalPath);
        }
    }

    private async Task FixMediaBadExtensions(SqliteConnection connection, string legacyDirectory,
        CancellationToken cancellationToken)
    {
        var toProcess = new List<(long Id, string RelativePath, string Path, string Name0)>();

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, relative_path, width, height, name, extension, add_date, last_view, " +
                           "is_private, from_file, file_hash, deleted FROM pictures " +
                           "WHERE extension = '.jpg:large' OR extension = '.jpg:d' ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var relativePath = RequiredText(row, "relative_path");
            var name = RequiredText(row, "name");
            var sourcePath = ResolveLegacyPath(relativePath, legacyDirectory);

            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Cannot fix non-existent path: {Path}", sourcePath);
                continue;
            }

            toProcess.Add((row.GetInt64("id"), relativePath, sourcePath, name));
        }

        foreach (var (id, relativePath, sourcePath, name) in toProcess)
        {
            logger.LogInformation("Attempting repair on missing extension: {Path}", sourcePath);

            string? targetExtension;
            MagickFormat format;
            try
            {
                var imageInfo = new MagickImageInfo(sourcePath);
                targetExtension = FileProbe.GetExtensionForFormat(imageInfo.Format);
                format = imageInfo.Format;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Cannot determine the format of file at: {Path}", sourcePath);
                continue;
            }

            if (targetExtension == null)
                throw new Exception("Could not determine extension for file: " + sourcePath);

            var finalPath = Path.ChangeExtension(sourcePath, targetExtension);

            if (File.Exists(finalPath))
            {
                // Sadly, this occurs sometimes, so we can't fix those
                logger.LogError("File already exists at path (can't rename a file with bad extension to it): {Path}",
                    finalPath);
                continue;
            }

            var newRelativePath = Path.ChangeExtension(relativePath, targetExtension);

            logger.LogInformation("File at path was actually a {Format} file: {Path}", format, sourcePath);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE pictures SET relative_path = @newRelativePath, extension = @newExtension, name = @newName " +
                "WHERE id = @id";
            command.Parameters.AddWithValue("newRelativePath", newRelativePath);
            command.Parameters.AddWithValue("newExtension", targetExtension);
            command.Parameters.AddWithValue("newName", Path.ChangeExtension(name, targetExtension));
            command.Parameters.AddWithValue("id", id);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);

            if (affected != 1)
            {
                throw new InvalidOperationException(
                    $"Expected to update exactly one row, but updated {affected} rows");
            }

            File.Move(sourcePath, finalPath);
            logger.LogInformation("File with bad extension was fixed and is now at path: {Path}", finalPath);
        }
    }

    private async Task ImportMediaAsync(SqliteConnection connection, Dictionary<long, long> appliedTagIds,
        Dictionary<long, long> collectionIds, string legacyDirectory, CancellationToken cancellationToken)
    {
        int ignored = 0;

        // Because otherwise we'd be just constantly committing to the new database, we use a buffer of images to
        // import and save at once
        var mediaBuffer = new List<(long OldId, MediaFile NewMedia, MediaImportInfo? ImportInfo, string OldPath)>();

        var mediaIds = new Dictionary<long, long>();
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, relative_path, width, height, name, extension, add_date, last_view, " +
                           "is_private, from_file, file_hash, deleted FROM pictures ORDER BY id",
                           cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (row.GetBoolean("deleted"))
                continue;

            var hash = RequiredText(row, "file_hash");

            // Ignore known bad media files
            if (skipMediaImportHashes.Contains(hash))
            {
                logger.LogInformation("Ignoring media file hash that is known bad: {Hash}", hash);
                continue;
            }

            var media = await dbContext.MediaFiles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Hash == hash, cancellationToken);

            if (media == null)
            {
                var sourcePath = ResolveLegacyPath(RequiredText(row, "relative_path"), legacyDirectory);
                string? customExtension = null;
                string validatedPath;
                try
                {
                    validatedPath = await ValidateOrRepairAsync(connection, sourcePath, row, hash, cancellationToken);
                }
                catch (IgnoreImportException e)
                {
                    logger.LogError(e, "Ignoring import of {SourcePath}", sourcePath);
                    ++ignored;
                    continue;
                }
                catch (ChangeHashException e)
                {
                    logger.LogInformation("Allowing hash of media at path to change: {SourcePath}", sourcePath);
                    logger.LogInformation("Hash is changing from {Hash1} to {Hash2}", hash, e.Hash);
                    hash = e.Hash;
                    validatedPath = e.Path;

                    // Still, make sure the new file pointed to by the updated hash is valid
                    await IsValidMediaAsync(e.Path, hash, cancellationToken);

                    // Check if the new hash is imported
                    media = await dbContext.MediaFiles.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(item => item.Hash == hash, cancellationToken);
                }
                catch (RetryAsDifferentMediaTypeException e)
                {
                    customExtension = Path.GetExtension(e.NewPath);
                    validatedPath = e.NewPath;
                    logger.LogInformation("Path is incorrect media type, overriding its extension to {Extension}",
                        customExtension);
                }
                catch (Exception e)
                {
                    if (skipImportBrokenFiles)
                    {
                        logger.LogError(e, "Skipping importing broken file! It will be missing from the database.");
                        ++ignored;
                        continue;
                    }

                    throw;
                }

                if (media == null)
                {
                    try
                    {
                        // Buffer this media import so that we can batch things into the DB
                        (media, var importInfo) =
                            await CreateMediaObject(connection, row, hash, validatedPath, customExtension,
                                cancellationToken);

                        mediaBuffer.Add((row.GetInt64("id"), media, importInfo, validatedPath));
                    }
                    catch (Exception e)
                    {
                        if (skipImportBrokenFiles)
                        {
                            logger.LogError(e,
                                "Skipping importing file with invalid data! It will be missing from the database.");
                            ++ignored;
                            continue;
                        }

                        throw;
                    }
                }
                else
                {
                    // This happens if recalculated hash has changed (and is now an already imported media)
                    logger.LogInformation("Media with hash {Hash} already exists at path {Path}", hash, validatedPath);
                    mediaIds[row.GetInt64("id")] = media.Id;
                }
            }
            else
            {
                if (media.Id <= 0)
                    throw new InvalidOperationException("Accidentally using non-initialized media");

                // Already exists so we can directly set the ID without needing to save to the database
                mediaIds[row.GetInt64("id")] = media.Id;
            }

            if (mediaBuffer.Count >= BulkImportImages)
                await ProcessImportBuffer(mediaBuffer, mediaIds, cancellationToken);

            if (mediaIds.Count % 50000 == 0)
                logger.LogInformation("Total loaded image objects: {Count}", mediaIds.Count);
        }

        // If we didn't end at exactly batch size, process the remaining
        if (mediaBuffer.Count > 0)
            await ProcessImportBuffer(mediaBuffer, mediaIds, cancellationToken);

        await ImportMediaRelationshipsAsync(connection, mediaIds, appliedTagIds, collectionIds, cancellationToken);

        if (ignored > 0)
        {
            logger.LogError("Some images were ignored due to import errors, failing");
            throw new Exception($"Some media failed to import (errors: {ignored})");
        }
    }

    private async Task ProcessImportBuffer(
        List<(long OldId, MediaFile NewMedia, MediaImportInfo? ImportInfo, string OldPath)> mediaBuffer,
        Dictionary<long, long> mediaIds, CancellationToken cancellationToken)
    {
        if (mediaBuffer.Count < 1)
            logger.LogWarning("Media buffer is empty");

        int doneImports = 0;
        var pendingImportInfo = new List<(MediaFile Media, MediaImportInfo ImportInfo)>();
        bool log = false;
        Exception? failure = null;

        foreach (var (_, newMedia, importInfo, oldPath) in mediaBuffer)
        {
            try
            {
                // This copies the file data to the new location
                await ImportMediaFile(cancellationToken, newMedia, oldPath);
                ++doneImports;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Canceled during image batch import, exiting loop but still trying to save DB");
                break;
            }
            catch (Exception e)
            {
                if (doneImports <= 0)
                    throw;

                logger.LogWarning("Failed one part of an image bundle, handling saving before throwing");
                failure = e;
                break;
            }

            if (importedTotalImages % 100 == 0)
                log = true;

            if (importInfo != null)
            {
                pendingImportInfo.Add((newMedia, importInfo));
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        // We do not want to cancel after writing images.
        // We save here to be able to access the IDs of the media files.
        await dbContext.SaveChangesAsync(CancellationToken.None);

        bool hadImportInfo = false;

        foreach (var (newMedia, importInfo) in pendingImportInfo)
        {
            if (newMedia.Id <= 0 || importInfo.MediaFileId != -1)
                throw new InvalidOperationException("New media was not saved, or import info was already used");

            importInfo.MediaFileId = newMedia.Id;
            dbContext.MediaImportInfos.Add(importInfo);
            hadImportInfo = true;
        }

        if (hadImportInfo)
        {
            // Need to then save again to get the import infos into the DB
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        if (failure != null)
            throw failure;

        if (log)
            logger.LogInformation("Imported media total: {Imported}", importedTotalImages);

        // Capture the IDs of things if not cancelled
        if (!cancellationToken.IsCancellationRequested)
        {
            foreach (var (oldId, newMedia, _, _) in mediaBuffer)
            {
                if (newMedia.Id <= 0)
                    throw new InvalidOperationException("New media was not saved");
                mediaIds[oldId] = newMedia.Id;
            }
        }

        mediaBuffer.Clear();
    }

    private async Task<(MediaFile Media, MediaImportInfo? ImportInfo)> CreateMediaObject(SqliteConnection connection,
        SqliteRow row, string hash, string validatedPath, string? customExtension, CancellationToken cancellationToken)
    {
        MediaFile media;

        string extension;
        if (string.IsNullOrWhiteSpace(customExtension))
        {
            extension = NormalizeExtension(RequiredText(row, "extension"));
        }
        else
        {
            extension = customExtension;
            if (!extension.StartsWith('.'))
                throw new Exception("Custom extension must start with a dot");

            logger.LogInformation(
                "Replacing database extension {Db} with {Custom} on import due to file type mismatch on image load",
                RequiredText(row, "extension"), customExtension);
        }

        MediaType type;
        try
        {
            type = MediaTypeExtensions.TypeFromExtension(extension);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Couldn't determine media type from extension, trying to figure it out");

            await using var stream = File.OpenRead(validatedPath);
            var imageInfo = new MagickImageInfo(stream);
            extension = FileProbe.GetExtensionForFormat(imageInfo.Format) ??
                        throw new Exception("Unknown Magick format");
            type = MediaTypeExtensions.TypeFromExtension(extension);
        }

        // Name already contains the extension, so we need to swap it to not get duplicates!
        media = new MediaFile(
            Path.ChangeExtension(RequiredText(row, "name") ?? throw new Exception("Extension change fail"), extension),
            hash)
        {
            MediaType = type,
            Width = row.GetInt32("width"),
            Height = row.GetInt32("height"),
            FrameCount = 1,
            FramesPerSecond = -1,
            ImportedAt = ParseDate(row.GetText("add_date")),
            LastViewed = ParseDate(row.GetText("last_view")),
            IsFavorited = await IsFavoritedAsync(connection, row.GetInt64("id"), cancellationToken),
        };

        var source = row.GetText("from_file");
        MediaImportInfo? importInfo = null;

        if (!string.IsNullOrWhiteSpace(source))
        {
            // Rudimentary detection what kind of source it is
            string? sourceLocalPath = null;
            string? sourceUrl = null;

            if (source.StartsWith("http"))
            {
                sourceUrl = source;
            }
            else
            {
                sourceLocalPath = source;
            }

            // This is a temporary one until the media is saved in the DB and then this is updated with the ID
            importInfo = new MediaImportInfo(-1)
            {
                SourcePath = sourceLocalPath,
                SourceUrl = sourceUrl,
            };
        }

        return (media, importInfo);
    }

    private async Task ImportMediaFile(CancellationToken cancellationToken, MediaFile media, string validatedPath)
    {
        await CopyIntoCurrentStorageAsync(validatedPath, media, cancellationToken);
        dbContext.MediaFiles.Add(media);
        ++importedTotalImages;
    }

    private async Task ImportMediaRelationshipsAsync(SqliteConnection connection, Dictionary<long, long> mediaIds,
        Dictionary<long, long> appliedTagIds, Dictionary<long, long> collectionIds, CancellationToken cancellationToken)
    {
        int skipped = 0;
        int processed = 0;

        // There's least number of these in the DB, so we import them first as this can use normal EF without being
        // way too slow
        await foreach (var row in ReadRowsAsync(connection, "SELECT collection, tag FROM collection_tag",
                           cancellationToken))
        {
            if (!collectionIds.TryGetValue(row.GetInt64("collection"), out var collectionId) ||
                !appliedTagIds.TryGetValue(row.GetInt64("tag"), out var appliedTagId))
            {
                ++skipped;
                continue;
            }

            var collection = await dbContext.Collections.Include(item => item.AppliedTags)
                .FirstAsync(item => item.Id == collectionId, cancellationToken);
            var applied = await dbContext.AppliedTags.FindAsync([appliedTagId], cancellationToken);
            if (applied != null && collection.AppliedTags.All(item => item.Id != applied.Id))
            {
                collection.AppliedTags.Add(applied);
                ++processed;
            }

            if (applied == null)
            {
                logger.LogWarning("Cannot find applied tag to add to collection with id {CollectionId}, tag {TagId}",
                    collectionId, appliedTagId);
            }

            if (processed % 5000 == 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Processed {Count} collection tags", processed);
            }
        }

        if (skipped > 0)
            logger.LogWarning("Skipped {Count} collection tags (probably unimported collections)", skipped);

        logger.LogInformation("Imported {Count} collection tags", processed);

        // Save after each block as we do have plenty of changes applied, and it would be better to find errors
        // before continuing
        await dbContext.SaveChangesAsync(cancellationToken);
        skipped = 0;
        processed = 0;

        // Then these two next ones are complicated because there's so much data we have to be more efficient.
        // This is a dictionary of *new* image ID to a list of *new* applied tag IDs.
        var imageTagGroups = new Dictionary<long, List<long>>();

        await foreach (var row in ReadRowsAsync(connection, "SELECT image, tag FROM image_tag", cancellationToken))
        {
            if (!mediaIds.TryGetValue(row.GetInt64("image"), out var mediaId) ||
                !appliedTagIds.TryGetValue(row.GetInt64("tag"), out var appliedTagId))
            {
                // Presumably, these are mostly failed media imports, so they are skipped
                ++skipped;
                continue;
            }

            if (!imageTagGroups.TryGetValue(mediaId, out var currentImageTags))
            {
                currentImageTags = new List<long>();
                imageTagGroups.Add(mediaId, currentImageTags);
            }

            currentImageTags.Add(appliedTagId);
        }

        if (skipped > 0)
            logger.LogWarning("Skipped {Count} image tags (probably unimported images)", skipped);

        int index = 0;
        int total = imageTagGroups.Count;
        int added = 0;

        foreach (var imageTagGroup in imageTagGroups)
        {
            // Write each image tag group in a single transaction
            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                foreach (var tagId in imageTagGroup.Value)
                {
                    added += await dbContext.Database.ExecuteSqlAsync(
                        $"INSERT OR IGNORE INTO MediaFileAppliedTags (MediaFilesId, AppliedTagsId) VALUES ({imageTagGroup.Key}, {tagId})",
                        cancellationToken: cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }

            processed += imageTagGroup.Value.Count;
            ++index;

            if (index % 10000 == 0)
            {
                logger.LogInformation("Processed {Count} out of {Total} image tag groups (new tags: {Added})", index, total, added);
            }
        }

        logger.LogInformation("Processed {Count} image tags and added {Added} new tags", processed, added);
        imageTagGroups.Clear();

        // We want to let go of this data
        // ReSharper disable once RedundantAssignment
        imageTagGroups = null;

        skipped = 0;
        processed = 0;
        added = 0;

        // This is a dictionary of *new* collection ID, and a list of new image IDs and their show order.
        var imageCollectionGroups = new Dictionary<long, List<(long Image, long ShowOrder)>>();

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT collection, image, show_order FROM collection_image",
                           cancellationToken))
        {
            if (!collectionIds.TryGetValue(row.GetInt64("collection"), out var collectionId) ||
                !mediaIds.TryGetValue(row.GetInt64("image"), out var mediaId))
            {
                // These should again be unimported resources we can skip
                ++skipped;
                continue;
            }

            if (!imageCollectionGroups.TryGetValue(collectionId, out var imageCollectionGroup))
            {
                imageCollectionGroup = new List<(long Image, long ShowOrder)>();
                imageCollectionGroups.Add(collectionId, imageCollectionGroup);
            }

            imageCollectionGroup.Add((mediaId, row.GetInt32("show_order")));
        }

        if (skipped > 0)
            logger.LogWarning("Skipped {Count} collection image assignments (probably unimported images)", skipped);

        total = imageCollectionGroups.Count;
        index = 0;

        foreach (var collectionGroup in imageCollectionGroups)
        {
            // Write each collection image list in a single transaction
            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                foreach (var (mediaId, showOrder) in collectionGroup.Value)
                {
                    added += await dbContext.Database.ExecuteSqlAsync(
                        $"INSERT OR IGNORE INTO CollectionItem (CollectionId, MediaFileId, SequenceNumber) VALUES ({collectionGroup.Key}, {mediaId}, {showOrder})",
                        cancellationToken: cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }

            processed += collectionGroup.Value.Count;
            ++index;

            if (index % 300 == 0)
            {
                logger.LogInformation("Processed {Count} out of {Total} collection image contents", index, total);
            }
        }

        logger.LogInformation("Processed {Count} collection items and added {Added} new items", processed, added);
        imageCollectionGroups.Clear();

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ValidateOrRepairAsync(SqliteConnection connection, string path, SqliteRow row,
        string expectedHash, CancellationToken cancellationToken)
    {
        Exception? failedException = null;

        try
        {
            if (await IsValidMediaAsync(path, expectedHash, cancellationToken))
                return path;
        }
        // Pass through exceptions
        catch (ChangeHashException)
        {
            throw;
        }
        catch (IgnoreImportException)
        {
            throw;
        }
        catch (RetryAsDifferentMediaTypeException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Validation failed with an error, trying potential repair");
            failedException = e;
        }

        logger.LogInformation("Attempting recovery on file: {Path}", path);

        var source = row.GetText("from_file");
        var fileName = RequiredText(row, "name") + NormalizeExtension(RequiredText(row, "extension"));
        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) ||
            (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT file_url FROM net_files WHERE preferred_name = $name OR file_url = $source LIMIT 1";
            command.Parameters.AddWithValue("$name", fileName);
            command.Parameters.AddWithValue("$source", source ?? string.Empty);
            var downloadUrl = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(downloadUrl))
                source = downloadUrl;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            // Ignore some sites that cannot be redownloaded
            if (source.Contains("example.com"))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // Disallows getting data again
            if (source.Contains("instagram."))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // Site gone
            if (source.Contains("getlazy.net"))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // This also seems gone
            if (source.Contains("pbs.twimg.com"))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // Discord applies 24-hour URL aliveness, so pretty useless to redownload
            if (source.Contains("discordapp.com") || source.Contains("media.discordapp.net"))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // These seem to always fail
            if (source.Contains("out.reddit.com") && source.Contains("imgur.com"))
                throw new IgnoreImportException("Cannot redownload from: " + source);

            // This seems to always give an HTML page even if it exists
            if (source.Contains("i.redd.it"))
                throw new IgnoreImportException("Cannot automatically redownload from: " + source);

            var extension = NormalizeExtension(RequiredText(row, "extension"));
            var target = Path.Combine(Path.GetTempPath(), "dualview-import-" + Guid.NewGuid());
            bool wasValid = false;

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (X11; Linux x86_64; rv:153.0) Gecko/20100101 Firefox/153.0");
                logger.LogInformation("Trying to redownload from: {Url}", uri);
                {
                    await using var input = await httpClient.GetStreamAsync(uri, cancellationToken);
                    await using var output = File.Create(target);
                    await input.CopyToAsync(output, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(extension) || extension == ".")
                {
                    try
                    {
                        var imageInfo = new MagickImageInfo(target);
                        extension = FileProbe.GetExtensionForFormat(imageInfo.Format);
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Downloaded file is not a valid image", e);
                    }

                    var old = target;
                    target = Path.ChangeExtension(target, extension);
                    File.Move(old, target);

                    // TODO: this needs to somehow signal to the importer what path to use to work
                }

                if (!File.Exists(target))
                    logger.LogError("Downloaded file does not exist");

                logger.LogInformation("Redownload succeeded, size: {Size}", new FileInfo(target).Length);

                // TODO: allowing extension to change here?
                // TODO: find out why this is always failing as not found
                if (await IsValidMediaAsync(target, expectedHash, cancellationToken, true))
                {
                    wasValid = true;
                    return target;
                }
            }
            catch (ChangeHashException)
            {
                // This should only be thrown when it was valid, so allow passing through
                logger.LogInformation("Downloaded file has different hash");
                wasValid = true;
                return target;
            }
            finally
            {
                if (File.Exists(target) && !wasValid)
                    File.Delete(target);
            }
        }

        logger.LogError("Unable to validate legacy path: {Path}", path);

        throw new InvalidDataException($"Legacy media '{fileName}' is missing, corrupt, or has an SHA mismatch",
            failedException);
    }

    private async Task<bool> IsValidMediaAsync(string path, string expectedHash,
        CancellationToken cancellationToken, bool allowHashChange = false)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("File does not exist at path to check it is valid for import: {Path}", path);
            return false;
        }

        await using var stream = File.OpenRead(path);
        var hash = await MediaHash.CalculateMediaHashAsync(stream, cancellationToken);
        if (hash != expectedHash)
        {
            // If hash is allowed to change, then signal that
            if (allowedHashChangeFiles.Contains(path) ||
                allowedHashChangeFolderPrefixes.Any(path.StartsWith) || allowHashChange)
            {
                throw new ChangeHashException(path, hash);
            }

            return false;
        }

        var extension = NormalizeExtension(Path.GetExtension(path));

        // We assume video files are valid for import
        if (!MediaTypeExtensions.TypeFromExtension(extension).IsImage())
            return true;

        try
        {
            using var frames = new MagickImageCollection();
            await frames.ReadAsync(path, cancellationToken);
            return frames.Count > 0;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to read image frames from {Path}", path);

            if (e.Message.Contains("starts with 0x89 0x50"))
            {
                await RetryAsDifferentMediaTypeAsync(path, ".png", "PNG", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains("starts with 0x47 0x49"))
            {
                await RetryAsDifferentMediaTypeAsync(path, ".gif", "GIF", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains("starts with 0x42 0x4d"))
            {
                await RetryAsDifferentMediaTypeAsync(path, ".bmp", "BMP", expectedHash, cancellationToken);
            }
            /*else if (e.Message.Contains("starts with 0x3c 0x21"))
            {
               // This is likely an HTML file, not an image
            }*/

            // await RetryAsDifferentMediaTypeAsync(path, ".webp", "WebP", expectedHash, cancellationToken);

            else if (e.Message.Contains("ImproperImageHeader `' @ error/png.c/ReadPNGImage/3956"))
            {
                // A .png file but isn't a png. Likely a jpeg
                await RetryAsDifferentMediaTypeAsync(path, ".jpg", "JPEG", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains("ImproperImageHeader `' @ error/gif.c/ReadGIFImage/1028"))
            {
                // A file pretending to be a GIF, but might be a jpg
                await RetryAsDifferentMediaTypeAsync(path, ".jpg", "JPEG", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains("CorruptImage `' @ error/webp.c/ReadWEBPImage/569"))
            {
                await RetryAsDifferentMediaTypeAsync(path, ".jpg", "JPEG", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains(
                         "starts with 0x52 0x49"))
            {
                await RetryAsDifferentMediaTypeAsync(path, ".webp", "WebP", expectedHash, cancellationToken);
            }
            else if (e.Message.Contains("ReadHEICImage/1036"))
            {
                // Try for fun reading this as JPEG
                await RetryAsDifferentMediaTypeAsync(path, ".jpg", "JPEG", expectedHash, cancellationToken);
            }

            return false;
        }
    }

    private async Task RetryAsDifferentMediaTypeAsync(string path, string extension, string mediaTypeName,
        string expectedHash, CancellationToken cancellationToken)
    {
        var newPath = Path.ChangeExtension(path, extension) ?? throw new Exception("New path is empty");

        logger.LogInformation("Retrying path as a {MediaType} file: {Path}", mediaTypeName, newPath);

        // Just for the extremely rare case of existing target, check its hash matches to not overwrite anything
        // important
        if (File.Exists(newPath))
        {
            await using var stream = File.OpenRead(path);
            var existingHash = await MediaHash.CalculateMediaHashAsync(stream, cancellationToken);
            if (existingHash != expectedHash)
            {
                logger.LogWarning("Existing file at {Path} has different hash, skipping", newPath);
                return;
            }
        }

        File.Copy(path, newPath, true);
        logger.LogInformation("Created duplicate file to test at: {Path}", newPath);

        if (await IsValidMediaAsync(newPath, expectedHash, cancellationToken))
        {
            // Not the cleanest to use exceptions for flow control, but this script file wasn't designed with a lot
            // of retry conditions. So we use exceptions for the rare file where the data is kind of incorrect,
            // but we can recover.
            logger.LogInformation("It is valid as {MediaType}, signalling up...", mediaTypeName);
            throw new RetryAsDifferentMediaTypeException(newPath);
        }

        logger.LogInformation("Deleting duplicate file as it is not valid as a {MediaType}: {Path}", mediaTypeName,
            newPath);

        File.Delete(newPath);
    }

    private async Task CopyIntoCurrentStorageAsync(string source, MediaFile media, CancellationToken cancellationToken)
    {
        if (settings == null)
            throw new Exception("Settings not loaded");

        var storage = settings.LocalMediaStorageLocation;
        if (string.IsNullOrWhiteSpace(storage))
        {
            logger.LogDebug("Using default storage location, which might not be wanted");
            storage = dataFolderService.GetDataFolderPath();
        }

        var destination = Path.Combine(storage, media.PathRelativeToStorage());
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException());
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            await using var input = File.OpenRead(source);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static string ResolveLegacyPath(string value, string legacyDirectory)
    {
        if (value.StartsWith(":?ocl/", StringComparison.Ordinal))
        {
            value = Path.Combine("public_collection", value[6..]);
        }
        else if (value.StartsWith(":?scl/", StringComparison.Ordinal))
        {
            value = Path.Combine("private_collection", value[6..]);
        }

        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(legacyDirectory, value));
    }

    private static string SanitizeName(string value)
    {
        return value.Replace('/', ' ').Replace('\\', ' ').Trim();
    }

    private static string NormalizeExtension(string value)
    {
        var extension = value.Trim();

        // For some reason there are some numeric suffixes
        if (value.EndsWith("jpg_1") || value.EndsWith("jpg_2") || value.EndsWith("jpg_3") || value.EndsWith("jpg_4"))
        {
            extension = ".jpg";
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static DateTime ParseDate(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : DateTime.UtcNow;
    }

    private static string RequiredText(SqliteRow row, string column)
    {
        return row.GetText(column) ?? throw new InvalidDataException($"Legacy row has no {column}");
    }

    private static async Task<bool> IsFavoritedAsync(SqliteConnection connection, long imageId,
        CancellationToken cancellationToken)
    {
        // Favouriting was not implemented, so nothing is favourited
        return false;
    }

    private static async IAsyncEnumerable<SqliteRow> ReadRowsAsync(SqliteConnection connection, string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            yield return new SqliteRow(reader);
    }

    private sealed class LegacyAppliedTag
    {
        public long TagId { get; }
        public List<long> ModifierIds { get; } = [];
        public long? CombinedWithId { get; set; }
        public string? CombineWord { get; set; }

        public LegacyAppliedTag(long tagId)
        {
            TagId = tagId;
        }
    }

    private sealed class SqliteRow
    {
        private readonly SqliteDataReader reader;

        public SqliteRow(SqliteDataReader reader) => this.reader = reader;

        public bool GetBoolean(string column)
        {
            return !reader.IsDBNull(reader.GetOrdinal(column)) &&
                   reader.GetInt64(reader.GetOrdinal(column)) != 0;
        }

        public int GetInt32(string column)
        {
            return reader.IsDBNull(reader.GetOrdinal(column))
                ? 0
                : reader.GetInt32(reader.GetOrdinal(column));
        }

        public long GetInt64(string column)
        {
            return reader.GetInt64(reader.GetOrdinal(column));
        }

        public string? GetText(string column)
        {
            return reader.IsDBNull(reader.GetOrdinal(column))
                ? null
                : reader.GetString(reader.GetOrdinal(column));
        }

        public override string ToString()
        {
            var builder = new StringBuilder("SqliteRow: ");

            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(reader.GetName(i));
                builder.Append('=');

                if (reader.IsDBNull(i))
                {
                    builder.Append("NULL");
                }
                else
                {
                    var value = reader.GetValue(i);
                    builder.Append(value is string text
                        ? $"\"{text}\""
                        : Convert.ToString(value, CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }
    }
}

internal class RetryAsDifferentMediaTypeException : Exception
{
    public RetryAsDifferentMediaTypeException(string newPath)
    {
        NewPath = newPath;
    }

    public string NewPath { get; }
}

internal class ChangeHashException : Exception
{
    public ChangeHashException(string path, string hash)
    {
        Path = path;
        Hash = hash;
    }

    public string Path { get; }
    public string Hash { get; }
}

internal class IgnoreImportException : Exception
{
    public IgnoreImportException(string message) : base(message)
    {
    }

    public IgnoreImportException(string message, Exception e) : base(message, e)
    {
    }
}
