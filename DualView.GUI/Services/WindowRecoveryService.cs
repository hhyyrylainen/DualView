using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Backend.Services;
using DualView.GUI.Models;
using DualView.GUI.ViewModels;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public sealed class WindowRecoveryService : IWindowRecoveryService, IDisposable
{
    private const int RecoveryDelayMilliseconds = 400;
    private const int WindowRecoveryDelayMilliseconds = 50;

    private readonly Lock synchronization = new();
    private readonly Dictionary<Window, WindowRecoveryEntry> entries = new();
    private readonly string recoveryFilePath;
    private readonly IClientDatabaseService databaseService;
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<WindowRecoveryService> logger;
    private readonly IBackgroundJobs backgroundJobs;
    private readonly Func<CancellationToken, Task> saveTask;

    private bool dirty;
    private bool disposed;
    private bool shutdown;

    private enum WindowRecoveryKind
    {
        MediaViewer,
        Collection,
        Singleton,
    }

    public WindowRecoveryService(IDataFolderService dataFolderService, IClientDatabaseService databaseService,
        IServiceProvider serviceProvider, ILogger<WindowRecoveryService> logger, IBackgroundJobs backgroundJobs)
    {
        recoveryFilePath = Path.Combine(dataFolderService.GetDataFolderPath(), "open-windows.json");
        this.databaseService = databaseService;
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.backgroundJobs = backgroundJobs;
        saveTask = SaveIfChangedAsync;
        backgroundJobs.Schedule(saveTask, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void RegisterMediaViewer(Window window, long mediaId, long? collectionId)
    {
        Register(window,
            new WindowRecoveryEntry
            {
                Kind = WindowRecoveryKind.MediaViewer,
                MediaId = mediaId,
                CollectionId = collectionId,
            });
    }

    public void RegisterCollectionWindow(Window window, long collectionId)
    {
        Register(window,
            new WindowRecoveryEntry
            {
                Kind = WindowRecoveryKind.Collection,
                CollectionId = collectionId,
            });
    }

    public void UpdateMediaViewer(Window window, long mediaId)
    {
        lock (synchronization)
        {
            if (!entries.TryGetValue(window, out var entry) || entry.Kind != WindowRecoveryKind.MediaViewer ||
                entry.MediaId == mediaId)
            {
                return;
            }

            entry.MediaId = mediaId;
            dirty = true;
        }
    }

    public void RegisterSingletonWindow(Window window, Type viewModelType)
    {
        Register(window,
            new WindowRecoveryEntry
            {
                Kind = WindowRecoveryKind.Singleton,
                ViewModelType = viewModelType.FullName,
            });
    }

    public void UnregisterWindow(Window window)
    {
        lock (synchronization)
        {
            if (entries.Remove(window))
                dirty = true;
        }
    }

    public async Task RecoverWindowsAsync(IWindowService windowService)
    {
        List<WindowRecoveryEntry> previousEntries;
        try
        {
            if (!File.Exists(recoveryFilePath))
                return;

            await using var file = File.OpenRead(recoveryFilePath);
            previousEntries = await JsonSerializer.DeserializeAsync<List<WindowRecoveryEntry>>(file) ?? new();
            File.Delete(recoveryFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read previous open window state");
            return;
        }

        await Task.Delay(RecoveryDelayMilliseconds);
        foreach (var entry in previousEntries)
        {
            lock (synchronization)
            {
                if (shutdown)
                    return;
            }

            try
            {
                await RecoverWindowAsync(entry, windowService);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to recover window of type {WindowType}", entry.Kind);
            }

            await Task.Delay(WindowRecoveryDelayMilliseconds);
        }
    }

    public void Clear()
    {
        lock (synchronization)
        {
            entries.Clear();
            dirty = false;
            shutdown = true;
        }

        try
        {
            if (File.Exists(recoveryFilePath))
                File.Delete(recoveryFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete open window state");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        backgroundJobs.CancelJob(saveTask);
    }

    private void Register(Window window, WindowRecoveryEntry entry)
    {
        lock (synchronization)
        {
            if (shutdown)
                return;

            entries[window] = entry;
            dirty = true;
        }
    }

    private Task SaveIfChangedAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        List<WindowRecoveryEntry> currentEntries;
        lock (synchronization)
        {
            if (!dirty || disposed)
                return Task.CompletedTask;

            currentEntries = entries.Values.ToList();
            dirty = false;
        }

        try
        {
            var directory = Path.GetDirectoryName(recoveryFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = recoveryFilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(currentEntries,
                new JsonSerializerOptions { WriteIndented = false }));
            File.Move(temporaryPath, recoveryFilePath, true);
        }
        catch (Exception ex)
        {
            lock (synchronization)
                dirty = true;
            logger.LogWarning(ex, "Failed to save open window state");
        }

        return Task.CompletedTask;
    }

    private async Task RecoverWindowAsync(WindowRecoveryEntry entry, IWindowService windowService)
    {
        switch (entry.Kind)
        {
            case WindowRecoveryKind.MediaViewer when entry.MediaId.HasValue:
            {
                var media = await databaseService.GetMediaFileAsync(entry.MediaId.Value);
                if (media != null)
                {
                    windowService.ShowMediaViewer(
                        new ServerMediaSource(new ConfiguredMediaInfo(media), serviceProvider),
                        entry.CollectionId.HasValue
                            ? new CollectionBrowse(entry.CollectionId.Value, databaseService, serviceProvider)
                            : null);
                }

                break;
            }
            case WindowRecoveryKind.Collection when entry.CollectionId.HasValue:
            {
                var collection = await databaseService.GetCollectionAsync(entry.CollectionId.Value);
                windowService.ShowWindow<MediaCollectionWindowViewModel>(viewModel =>
                    _ = viewModel.Initialize(entry.CollectionId.Value, collection?.Name));
                break;
            }
            case WindowRecoveryKind.Singleton when entry.ViewModelType != null:
                RecoverSingleton(entry.ViewModelType, windowService);
                break;
        }
    }

    private static void RecoverSingleton(string viewModelTypeName, IWindowService windowService)
    {
        var viewModelType = typeof(MainWindowViewModel).Assembly.GetType(viewModelTypeName);
        if (viewModelType == null || viewModelType.Namespace == null ||
            !viewModelType.Namespace.StartsWith("DualView.GUI.ViewModels", StringComparison.Ordinal))
        {
            return;
        }

        var method = typeof(IWindowService).GetMethods()
            .SingleOrDefault(candidate => candidate.Name == nameof(IWindowService.ShowSingletonWindow) &&
                                          candidate.IsGenericMethodDefinition &&
                                          candidate.GetParameters().Length == 0);
        method?.MakeGenericMethod(viewModelType).Invoke(windowService, null);
    }

    private sealed class WindowRecoveryEntry
    {
        public WindowRecoveryKind Kind { get; set; }
        public long? MediaId { get; set; }
        public long? CollectionId { get; set; }
        public string? ViewModelType { get; set; }
    }
}
