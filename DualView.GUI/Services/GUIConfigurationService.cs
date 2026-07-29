using System;
using System.IO;
using Backend.Services;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public class GUIConfigurationService : IGuiConfigurationService
{
    private const string ConfigFileName = "gui-config.yaml";

    private readonly ILogger<GUIConfigurationService> logger;

    public GUIConfigurationService(ILogger<GUIConfigurationService> logger, IDataFolderService dataFolderService)
    {
        this.logger = logger;

        var expectedConfigFile = Path.Join(dataFolderService.GetDataFolderPath(), ConfigFileName);

        ConfigFileFormat tempConfig;

        if (File.Exists(expectedConfigFile))
        {
            var yaml = new YamlDotNet.Serialization.DeserializerBuilder().WithCaseInsensitivePropertyMatching().Build();
            tempConfig = yaml.Deserialize<ConfigFileFormat>(File.ReadAllText(expectedConfigFile));

            logger.LogInformation("Loaded GUI config from {Path}", expectedConfigFile);
        }
        else
        {
            logger.LogInformation("GUI config file doesn't exist at {Path}", expectedConfigFile);

            // Use default values
            tempConfig = new ConfigFileFormat();
        }

        // Copy to our properties
        try
        {
            BackendUrl = new Uri(tempConfig.BackendUrl);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to parse config file");
            throw;
        }

        if (!string.IsNullOrEmpty(tempConfig.FfmpegLibPath))
            FfmpegLibraryPath = tempConfig.FfmpegLibPath;
    }

    public Uri BackendUrl { get; set; }
    public string? FfmpegLibraryPath { get; set; }

    private class ConfigFileFormat
    {
        public string BackendUrl { get; set; } = "http://localhost:7362";

        public string FfmpegLibPath { get; set; } = "";
    }
}
