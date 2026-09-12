using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DualView.GUI.ViewModels;
using DualView.GUI.Views;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI;

public class App : Application
{
    private IServiceScope? serviceScope;
    private ILoggingService? logger;
    private bool running;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        running = true;

        var loadStaticAssets = Task.Run(() =>
        {
            // Load always loaded image resources now that Avalonia has started
            EmbeddedResourceImage.GetResource(EmbeddedResourceImage.FolderIcon);
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            if (Program.ServiceProvider != null)
            {
                serviceScope = Program.ServiceProvider.CreateScope();
                logger = serviceScope.ServiceProvider.GetRequiredService<ILoggingService>();
                logger.Info("Application framework initialized");
                var windowRecoveryService = serviceScope.ServiceProvider.GetRequiredService<IWindowRecoveryService>();

                // If we are not connected to the server, show the connection failure window instead of the usual main
                // window
                var backendStatusService = serviceScope.ServiceProvider.GetRequiredService<IBackendStatusService>();

                if (!backendStatusService.IsConnected)
                {
                    logger.Info("Creating connection failure window");
                    var failedScope = Program.ServiceProvider.CreateScope();
                    var view = failedScope.ServiceProvider.GetRequiredService<ConnectionFailedViewModel>();

                    if (!string.IsNullOrEmpty(Program.ConnectionErrorMessage))
                        view.ExtraMessage = Program.ConnectionErrorMessage;

                    var window = new ConnectionFailedWindow
                    {
                        DataContext = view,
                    };

                    // Remember to dispose of the scope when the window is closed, otherwise it'll keep running things
                    window.Closed += (_, _) => failedScope.Dispose();

                    desktop.MainWindow = window;
                }
                else
                {
                    // Create the main window with its ViewModel from the DI container
                    logger.Info("Creating main window");
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = serviceScope.ServiceProvider.GetRequiredService<MainWindowViewModel>(),
                    };
                }

                // Register for shutdown
                desktop.Exit += OnApplicationExit;

                // Restore state from before crash
                _ = windowRecoveryService.RecoverWindowsAsync(
                    serviceScope.ServiceProvider.GetRequiredService<IWindowService>());

                // UI Thread Watchdog to detect freezes
                StartUiWatchdog();
            }
            else
            {
                // Fallback if ServiceProvider is null
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }
        }

        loadStaticAssets.Wait();

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowOrActivateMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Handle switching to the main window from the failed window
            if (desktop.MainWindow is ConnectionFailedWindow)
            {
                logger?.Info("Switching from failure window to the main window");
                desktop.MainWindow = null;
            }
            else
            {
                if (desktop.MainWindow is { IsVisible: true })
                {
                    desktop.MainWindow.Activate();
                    return;
                }
            }

            logger?.LogInformation("Showing main window again");

            // Need a new main window
            desktop.MainWindow = new MainWindow
            {
                DataContext = serviceScope?.ServiceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.MainWindow.Show();
        }
    }

    private void StartUiWatchdog()
    {
        _ = Task.Run(async () =>
        {
            // Wait a bit for the app to settle
            await Task.Delay(5000);

            var watchdogLogger = serviceScope?.ServiceProvider.GetService<ILogger<App>>();
            if (watchdogLogger == null)
            {
                Console.WriteLine("No logger for watchdog!");
                return;
            }

            watchdogLogger.LogInformation("UI Thread Watchdog started");

            while (running)
            {
                var pingReceived = false;
                var startTime = Stopwatch.GetTimestamp();
                Dispatcher.UIThread.Post(() => pingReceived = true);

                await Task.Delay(2000);

                if (!pingReceived)
                {
                    var hangTime = (double)(Stopwatch.GetTimestamp() - startTime) / Stopwatch.Frequency;
                    watchdogLogger.LogWarning("UI Thread Hang Detected! No response for {HangTime:F2}s", hangTime);

                    // Wait until it responds again to report how long it was stuck
                    while (!pingReceived)
                    {
                        await Task.Delay(500);
                    }

                    hangTime = (double)(Stopwatch.GetTimestamp() - startTime) / Stopwatch.Frequency;
                    watchdogLogger.LogWarning("UI Thread Resumed after {HangTime:F2}s", hangTime);
                }
            }
        });
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        running = false;
        serviceScope?.ServiceProvider.GetService<IWindowRecoveryService>()?.Clear();
        logger?.Info("Application UI shutting down");
        logger = null;

        // Dispose of the service scope when the application exits
        serviceScope?.Dispose();
        serviceScope = null;

        Program.OnApplicationExit(sender, e);
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // TODO: find a replacement for this:
        /*// Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }*/
    }
}
