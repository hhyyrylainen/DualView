using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Backend.Plugins;

public class PluginRegistry : IPluginRegistry
{
    private readonly ILogger<PluginRegistry> logger;

    private readonly List<IPlugin> plugins = new();
    private readonly List<IRemoteDownloadPlugin> remoteDownloadPlugins = new();

    public PluginRegistry(ILogger<PluginRegistry> logger)
    {
        this.logger = logger;
    }

    public async Task InitializeAllPlugins(IServiceProvider serviceProvider)
    {
        var pluginTypes = new HashSet<Type>();
        var pluginType = typeof(IPlugin);

        Assembly?[] rootAssemblies = [Assembly.GetEntryAssembly(), Assembly.GetExecutingAssembly()];
        var assemblies = new HashSet<Assembly>();

        foreach (var assembly in rootAssemblies)
        {
            if (assembly == null)
                continue;

            assemblies.Add(assembly);

            foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    assemblies.Add(Assembly.Load(referencedAssemblyName));
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Could not load referenced assembly {Assembly}",
                        referencedAssemblyName.FullName);
                }
            }
        }

        // TODO: implement loading extra plugins based on file paths

        foreach (var assembly in assemblies)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).Cast<Type>().ToArray();
                logger.LogDebug(e, "Could not load all types from assembly {Assembly}", assembly.FullName);
            }

            foreach (var type in types.Where(t =>
                         t is { IsClass: true, IsAbstract: false } &&
                         pluginType.IsAssignableFrom(t)))
            {
                pluginTypes.Add(type);
            }
        }

        // Then create and register all plugins
        foreach (var pluginClass in pluginTypes)
        {
            try
            {
                var plugin = (IPlugin?)Activator.CreateInstance(pluginClass, serviceProvider) ??
                             throw new InvalidOperationException(
                                 $"Failed to create plugin class of type: {pluginClass.Name}, " +
                                 $"does it have a suitable constructor?");

                await plugin.OnStart(this);

                // Check name conflicts
                foreach (var alreadyRegistered in plugins)
                {
                    if (alreadyRegistered.Name == plugin.Name)
                        throw new InvalidOperationException($"Plugin name conflict: {plugin.Name}");
                }

                plugins.Add(plugin);

                // Extra plugin assemblies can be loaded from the plugin directory.
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (plugin is IRemoteDownloadPlugin remoteDownloadPlugin)
                {
                    remoteDownloadPlugins.Add(remoteDownloadPlugin);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error initializing plugin {Plugin}", pluginClass.Name);
                throw;
            }
        }

        logger.LogDebug("Registered plugins: {Plugins}", string.Join(", ", plugins.Select(p => p.Name)));
        logger.LogInformation("Total plugins registered: {Count}", plugins.Count);
    }

    public async Task StopAllPlugins()
    {
        // Specific plugin lists have duplicates of the main list, so just clear
        remoteDownloadPlugins.Clear();

        foreach (var plugin in plugins)
        {
            try
            {
                await plugin.OnStop();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error stopping plugin {Plugin}", plugin.Name);
            }
        }

        plugins.Clear();
        logger.LogInformation("Stopped all plugins");
    }

    public IPlugin? GetPlugin(string name)
    {
        foreach (var plugin in plugins)
        {
            if (plugin.Name == name)
                return plugin;
        }

        return null;
    }

    public IReadOnlyList<IRemoteDownloadPlugin> GetRemoteDownloadPlugins()
    {
        return remoteDownloadPlugins;
    }

    public IReadOnlyList<IPlugin> GetAllPlugins()
    {
        return plugins;
    }
}
