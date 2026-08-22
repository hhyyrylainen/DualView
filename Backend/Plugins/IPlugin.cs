namespace Backend.Plugins;

/// <summary>
///   Plugin interface.
///   Note all plugins need a constructor that takes an <see cref="IServiceProvider"/> as a parameter.
/// </summary>
public interface IPlugin
{
    public string Name { get; }
    public string Version { get; }

    public Task OnStart(IPluginRegistry registry);
    public Task OnStop();
}
