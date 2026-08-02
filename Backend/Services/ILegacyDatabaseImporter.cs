namespace Backend.Services;

public interface ILegacyDatabaseImporter
{
    public Task ImportAsync(string databasePath, CancellationToken cancellationToken = default);
}
