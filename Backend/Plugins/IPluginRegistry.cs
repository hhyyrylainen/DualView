namespace Backend.Plugins;

/// <summary>
///   Manages loading and unloading of plugins.
/// </summary>
public interface IPluginRegistry
{
    public Task InitializeAllPlugins(IServiceProvider serviceProvider);
    public Task StopAllPlugins();

    public IPlugin? GetPlugin(string name);

    public IReadOnlyList<IRemoteDownloadPlugin> GetRemoteDownloadPlugins();

    public IReadOnlyList<IPlugin> GetAllPlugins();
}
