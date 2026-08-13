using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using DualView.GUI.Models;
using DualView.GUI.ServiceProxies;
using DualView.GUI.Services;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using DualView.GUI.ViewModels;
using Avalonia.Logging;
using Backend.Services;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Microsoft.Extensions.Logging;

namespace DualView.GUI;

sealed class Program
{
    public static readonly bool SuppressAvaloniaIBusBug = true;

    // Add a static property to hold the ServiceProvider
    public static IServiceProvider? ServiceProvider { get; private set; }

    public static string? ConnectionErrorMessage { get; private set; }

    private static ILoggingService? logger;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet, and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Set up initial logger for bootstrap phase
        var bootstrapLogger = new NLogLoggingService("Bootstrap");

        try
        {
            // Setup data folders and NLog configuration
            var dataFolderService = new DataFolderService("DualView", bootstrapLogger);
            dataFolderService.EnsureDataFoldersExist();

            // Configure NLog
            string logDirectory = dataFolderService.GetLogsFolderPath();
            NLogConfigurator.Configure(logDirectory, AppComponent.DesktopGUI);

            // Update the logger after NLog is configured
            logger = new NLogLoggingService("Main");
            logger.Info("Application starting...");

            // Set up the dependency injection container
            var services = new ServiceCollection();
            ConfigureServices(services, dataFolderService);
            ServiceProvider = services.BuildServiceProvider();
            StartInitialServices(ServiceProvider);
        }
        catch (Exception ex)
        {
            bootstrapLogger.Fatal(ex, "Fatal error during application startup");
            return -1;
        }

        var backendStatusService = ServiceProvider.GetRequiredService<IBackendStatusService>();

        try
        {
            // Wait until the backend is available, or it has failed after a while
            logger.LogInformation("Connecting to backend...");
            backendStatusService.StartBackendConnectionAsync().Wait(TimeSpan.FromSeconds(30));

            if (!backendStatusService.IsConnected)
            {
                throw new Exception(
                    "Backend is not available. Make sure backend is running and GUI settings file is correct.");
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Failed to connect to backend");
            ConnectionErrorMessage = e.Message;
        }

        backendStatusService.BeginMonitoring();

        try
        {
            // Start the Avalonia application
            logger.Info("Starting Avalonia UI");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            if (Debugger.IsAttached)
                Debugger.Break();

            logger.Fatal(e, "Fatal error from Avalonia");

            // TODO: show a popup error?

            return -2;
        }
        finally
        {
            try
            {
                EmbeddedResourceImage.OnShutdown();
            }
            catch (Exception e)
            {
                logger.Error(e, "Error during shutdown");
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var appBuilder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWayland()
            .WithInterFont();

        var levels = new Dictionary<LogEventLevel, NLog.LogLevel>
        {
            { LogEventLevel.Debug, NLog.LogLevel.Debug },
            { LogEventLevel.Error, NLog.LogLevel.Error },
            { LogEventLevel.Fatal, NLog.LogLevel.Fatal },
            { LogEventLevel.Information, NLog.LogLevel.Info },
            { LogEventLevel.Verbose, NLog.LogLevel.Trace },
            { LogEventLevel.Warning, NLog.LogLevel.Warn },
        };

        // Forward Avalonia logs to NLog
        foreach (var logLevelPair in levels)
        {
            appBuilder.LogToDelegate(message =>
            {
                // Suppress messages about Avalonia bug: https://github.com/AvaloniaUI/Avalonia/issues/15551
                if (SuppressAvaloniaIBusBug &&
                    (message.Contains("Method Destroy is not implemented on interface org.freedesktop.IBus.Service") ||
                     message.Contains("org.freedesktop.DBus.Error.UnknownMethod: Object does not exist")))
                {
                    return;
                }

                // TODO: figure out how to get the area from the log message
                // var avaloniaLogger = LogManager.GetLogger($"Avalonia.{area}");
                var avaloniaLogger = LogManager.GetLogger("Avalonia");
                avaloniaLogger.Log(logLevelPair.Value, message);
            }, logLevelPair.Key);
        }

        return appBuilder;
    }

    public static void OnApplicationExit(object? sender, EventArgs args)
    {
        logger?.Info("Application shutting down...");

        if (ServiceProvider != null)
        {
            try
            {
                var backgroundJobs = ServiceProvider.GetService<IBackgroundJobs>();
                backgroundJobs?.Stop(true, TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                logger?.Error(e, "Failed to stop background jobs");
            }
        }

        // Clean up the ServiceProvider
        if (ServiceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }

        // Flush and shutdown NLog
        LogManager.Shutdown();
    }

    private static void ConfigureServices(IServiceCollection services, IDataFolderService dataFolderService)
    {
        // Configure Microsoft logging with NLog
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new NLogLoggerProvider());
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        // Register our custom logging service
        services.AddSingleton<ILoggingService>(_ =>
            new NLogLoggingService("DualView.GUI"));

        // Register data folder service
        services.AddSingleton(dataFolderService);

        // Register services
        services.AddSingleton<ISignalRService, GUISignalRService>();
        services.AddSingleton<IRealtimeDataUpdateService, GUIRealtimeUpdateService>();
        services.AddSingleton<IBackgroundJobs, BackgroundJobs>();

        services.AddSingleton<IGuiConfigurationService, GUIConfigurationService>();
        services.AddSingleton<IClientDatabaseService, GUIDatabaseProxy>();
        services.AddSingleton<IBackendAPI, GUIBackendAPIProxy>();
        services.AddSingleton<IBackendStatusService, BackendStatusService>();

        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<ICachedIPResolver, CachedIPResolver>();
        services.AddSingleton<FfmpegDecoderService>();
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<AboutWindowViewModel>();
        services.AddTransient<ConnectionFailedViewModel>();
        services.AddTransient<ErrorWindowViewModel>();
        services.AddTransient<TextInputWindowViewModel>();
        services.AddTransient<InformationWindowViewModel>();
        services.AddTransient<TextEditWindowViewModel>();
        services.AddTransient<MediaCollectionWindowViewModel>();
        services.AddTransient<ImportWindowViewModel>();
        services.AddTransient<ConfirmationWindowViewModel>();
        services.AddTransient<RestoreDeletedWindowViewModel>();
        services.AddTransient<OperationStatusWindowViewModel>();
        services.AddTransient<MediaEditorWindowViewModel>();
        services.AddTransient<MediaEditSelectorWindowViewModel>();
        services.AddTransient<MediaPickerWindowViewModel>();
        services.AddTransient<EditMediaFoldersWindowViewModel>();
        services.AddTransient<TagManagerWindowViewModel>();
        services.AddTransient<MaintenanceToolsWindowViewModel>();
        services.AddTransient<RenameWindowViewModel>();
        services.AddTransient<ReorderWindowViewModel>();
        services.AddTransient<RemoveFromFoldersWindowViewModel>();
        services.AddTransient<UploadWindowViewModel>();
    }

    private static void StartInitialServices(IServiceProvider serviceProvider)
    {
        // Configure ImageMagick limits
        ResourceLimits.LimitMemory(new Percentage(25));

        // Limit to about 1 gigabyte at most
        ulong reasonableMemoryLimit = 1024L * 1024 * 1024;

        if (ResourceLimits.Memory >= reasonableMemoryLimit)
        {
            ResourceLimits.Memory = reasonableMemoryLimit;
        }

        var backgroundJobs = serviceProvider.GetRequiredService<IBackgroundJobs>();
        backgroundJobs.Start();

        // Make sure some services are started
        var config = serviceProvider.GetRequiredService<IGuiConfigurationService>();

        ServerMediaSource.SetupHttpClient(config.BackendUrl);
    }
}
