using Backend.Services;

namespace DualView.Server.Services;

public class ServerConfigurationService : IServerConfigurationService
{
    private const string ConfigFileName = "server-config.yaml";

    private readonly ILogger logger;

    public string? ListenUrl { get; }
    public string? DatabaseFilePath { get; }
    public string? LegacyDatabaseFilePath { get; }

    public ServerConfigurationService(ILogger logger, IDataFolderService dataFolderService)
    {
        this.logger = logger;

        var expectedConfigFile = Path.Join(dataFolderService.GetDataFolderPath(), ConfigFileName);

        ConfigFileFormat tempConfig;

        if (File.Exists(expectedConfigFile))
        {
            try
            {
                var yaml = new YamlDotNet.Serialization.DeserializerBuilder().WithCaseInsensitivePropertyMatching().Build();
                tempConfig = yaml.Deserialize<ConfigFileFormat>(File.ReadAllText(expectedConfigFile));

                logger.LogInformation("Loaded server config from {Path}", expectedConfigFile);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to parse server config file at {Path}", expectedConfigFile);
                tempConfig = new ConfigFileFormat();
            }
        }
        else
        {
            logger.LogInformation("Server config file doesn't exist at {Path}, using defaults", expectedConfigFile);
            tempConfig = new ConfigFileFormat();
        }

        if (!string.IsNullOrEmpty(tempConfig.ListenUrl))
            ListenUrl = tempConfig.ListenUrl;

        if (!string.IsNullOrEmpty(tempConfig.DatabaseFile))
            DatabaseFilePath = tempConfig.DatabaseFile;

        if (!string.IsNullOrEmpty(tempConfig.LegacyDatabaseFile))
            LegacyDatabaseFilePath = tempConfig.LegacyDatabaseFile;
    }

    private class ConfigFileFormat
    {
        public string? ListenUrl { get; set; }

        public string? DatabaseFile { get; set; }

        /// <summary>
        ///   When set, the server imports this DualView++ database before accepting requests.
        /// </summary>
        public string? LegacyDatabaseFile { get; set; }
    }
}
