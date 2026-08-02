using System.Globalization;
using System.Text;
using Backend.Database;
using Backend.Models;
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
    private readonly ILogger<LegacyDatabaseImporter> logger;
    private readonly AppDbContext dbContext;
    private readonly IDataFolderService dataFolderService;
    private DualViewSettings? settings;

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

    private async Task ImportMediaAsync(SqliteConnection connection, Dictionary<long, long> appliedTagIds,
        Dictionary<long, long> collectionIds, string legacyDirectory, CancellationToken cancellationToken)
    {
        int imported = 0;

        var mediaIds = new Dictionary<long, long>();
        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT id, relative_path, width, height, name, extension, add_date, last_view, " +
                           "is_private, from_file, file_hash, deleted FROM pictures ORDER BY id",
                           cancellationToken))
        {
            if (row.GetBoolean("deleted"))
                continue;

            var hash = RequiredText(row, "file_hash");

            var media = await dbContext.MediaFiles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Hash == hash, cancellationToken);

            if (media == null)
            {
                var sourcePath = ResolveLegacyPath(RequiredText(row, "relative_path"), legacyDirectory);
                var validatedPath = await ValidateOrRepairAsync(connection, sourcePath, row, hash, cancellationToken);
                var extension = NormalizeExtension(RequiredText(row, "extension"));
                media = new MediaFile(RequiredText(row, "name") + extension, hash)
                {
                    MediaType = MediaTypeExtensions.TypeFromExtension(extension),
                    Width = row.GetInt32("width"),
                    Height = row.GetInt32("height"),
                    FrameCount = 1,
                    FramesPerSecond = -1,
                    ImportedAt = ParseDate(row.GetText("add_date")),
                    LastViewed = ParseDate(row.GetText("last_view")),
                    IsFavorited = await IsFavoritedAsync(connection, row.GetInt64("id"), cancellationToken),
                };
                await CopyIntoCurrentStorageAsync(validatedPath, media, cancellationToken);
                dbContext.MediaFiles.Add(media);
                await dbContext.SaveChangesAsync(cancellationToken);

                var source = row.GetText("from_file");
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

                    dbContext.MediaImportInfos.Add(new MediaImportInfo(media.Id)
                    {
                        SourcePath = sourceLocalPath,
                        SourceUrl = sourceUrl,
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                if (++imported % 100 == 0)
                    logger.LogInformation("Imported media total: {Imported}", imported);
            }

            mediaIds[row.GetInt64("id")] = media.Id;
        }

        await ImportMediaRelationshipsAsync(connection, mediaIds, appliedTagIds, collectionIds, cancellationToken);
    }

    private async Task ImportMediaRelationshipsAsync(SqliteConnection connection, Dictionary<long, long> mediaIds,
        Dictionary<long, long> appliedTagIds, Dictionary<long, long> collectionIds, CancellationToken cancellationToken)
    {
        await foreach (var row in ReadRowsAsync(connection, "SELECT image, tag FROM image_tag", cancellationToken))
        {
            if (!mediaIds.TryGetValue(row.GetInt64("image"), out var mediaId) ||
                !appliedTagIds.TryGetValue(row.GetInt64("tag"), out var appliedTagId))
            {
                continue;
            }

            var media = await dbContext.MediaFiles.Include(item => item.AppliedTags).FirstAsync(
                item => item.Id == mediaId,
                cancellationToken);
            var applied = await dbContext.AppliedTags.FindAsync([appliedTagId], cancellationToken);
            if (applied != null && media.AppliedTags.All(item => item.Id != applied.Id))
                media.AppliedTags.Add(applied);
        }

        await foreach (var row in ReadRowsAsync(connection, "SELECT collection, tag FROM collection_tag",
                           cancellationToken))
        {
            if (!collectionIds.TryGetValue(row.GetInt64("collection"), out var collectionId) ||
                !appliedTagIds.TryGetValue(row.GetInt64("tag"), out var appliedTagId))
            {
                continue;
            }

            var collection = await dbContext.Collections.Include(item => item.AppliedTags)
                .FirstAsync(item => item.Id == collectionId, cancellationToken);
            var applied = await dbContext.AppliedTags.FindAsync([appliedTagId], cancellationToken);
            if (applied != null && collection.AppliedTags.All(item => item.Id != applied.Id))
                collection.AppliedTags.Add(applied);
        }

        await foreach (var row in ReadRowsAsync(connection,
                           "SELECT collection, image, show_order FROM collection_image",
                           cancellationToken))
        {
            if (!collectionIds.TryGetValue(row.GetInt64("collection"), out var collectionId) ||
                !mediaIds.TryGetValue(row.GetInt64("image"), out var mediaId))
            {
                continue;
            }

            var exists = await dbContext.Set<CollectionItem>().AnyAsync(item => item.CollectionId == collectionId &&
                item.MediaFileId == mediaId, cancellationToken);
            if (!exists)
            {
                dbContext.Set<CollectionItem>().Add(new CollectionItem
                {
                    CollectionId = collectionId,
                    MediaFileId = mediaId,
                    SequenceNumber = row.GetInt32("show_order"),
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ValidateOrRepairAsync(SqliteConnection connection, string path, SqliteRow row,
        string expectedHash, CancellationToken cancellationToken)
    {
        if (await IsValidMediaAsync(path, expectedHash, cancellationToken))
            return path;

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
            var target = Path.Combine(Path.GetTempPath(),
                "dualview-import-" + Guid.NewGuid() + NormalizeExtension(RequiredText(row, "extension")));
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (X11; Linux x86_64; rv:153.0) Gecko/20100101 Firefox/153.0");
                await using (var input = await httpClient.GetStreamAsync(uri, cancellationToken))
                await using (var output = File.Create(target))
                    await input.CopyToAsync(output, cancellationToken);

                if (await IsValidMediaAsync(target, expectedHash, cancellationToken))
                    return target;
            }
            finally
            {
                if (File.Exists(target) && !await IsValidMediaAsync(target, expectedHash, cancellationToken))
                    File.Delete(target);
            }
        }

        throw new InvalidDataException($"Legacy media '{fileName}' is missing, corrupt, or has a SHA mismatch");
    }

    private async Task<bool> IsValidMediaAsync(string path, string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = await MediaHash.CalculateMediaHashAsync(stream, cancellationToken);
        if (hash != expectedHash)
            return false;

        var extension = NormalizeExtension(Path.GetExtension(path));

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
            return false;
        }
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
