namespace DualView.Server.Services;

public interface IServerConfigurationService
{
    public string? GUILauncher { get; }
    public string? ListenUrl { get; }
    public string? DatabaseFilePath { get; }
    public string? LegacyDatabaseFilePath { get; }
    public string? LegacyCollectionRootPath { get; }
}
