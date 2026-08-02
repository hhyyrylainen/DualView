namespace Backend.Services;

public interface ILegacyDatabaseImporter
{
    public Task ImportAsync(string databasePath, string legacyRootCollectionPath,
        CancellationToken cancellationToken = default);
}
