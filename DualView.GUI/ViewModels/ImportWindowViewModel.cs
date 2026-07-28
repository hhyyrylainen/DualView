using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ImportWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<ImportWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;

    public string TargetImportPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "Inputs/Visual";

    public string ExtraPathToAddTo
    {
        get;
        set => SetProperty(ref field, value);
    } = "Inputs/Visual";

    public bool DeleteAfterImport
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ImportAlphaAsMask
    {
        get;
        set => SetProperty(ref field, value);
    } = false;

    /// <summary>
    ///   When set, images will be auto-added to this dataset during import using sidecar .txt files for tags.
    /// </summary>
    public string? DatasetForAutoIncludeName
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ImportWithoutCaptions
    {
        get;
        set => SetProperty(ref field, value);
    } = false;

    public bool AddDatasetDuplicates
    {
        get;
        set => SetProperty(ref field, value);
    } = false;

    public string StatusText
    {
        get;
        set => SetProperty(ref field, value);
    } = "Not importing";

    public ObservableCollection<MediaViewerViewModel> MediaToImport { get; } = new();

    public HamburgerMenuViewModel Hamburger { get; }

    // Design time constructor
    public ImportWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();

        // Some test items to test the scroll
        for (int i = 0; i < 14; ++i)
        {
            MediaToImport.Add(new MediaViewerViewModel
            {
                Name = $"Test {i}",
                Selected = i % 3 == 0,
            });
        }
    }

    [ActivatorUtilitiesConstructor]
    public ImportWindowViewModel(ILogger<ImportWindowViewModel> logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, IBackendStatusService backendStatusService,
        IBackendAPI? backendAPI, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();
    }

    public void OpenMediaWindow()
    {
        windowService?.ShowSingletonWindow<MediaCollectionWindowViewModel>();
    }

    public void StartImport()
    {
        if (string.IsNullOrWhiteSpace(TargetImportPath))
        {
            StatusText = "No target path specified";
            return;
        }

        var selected = MediaToImport.Where(m => m.Selected).ToList();

        if (selected.Count < 1)
        {
            StatusText = "No media selected (add media and select them first)";
            return;
        }

        StatusText = $"Importing {selected.Count} images";
        _ = PerformImport(selected);
    }

    public void Clear()
    {
        foreach (var media in MediaToImport)
        {
            media.Dispose();
        }

        MediaToImport.Clear();
    }

    public void DeselectAll()
    {
        foreach (var media in MediaToImport)
        {
            media.Selected = false;
        }
    }

    public void AddImages(List<string> imagePaths)
    {
        if (clientDatabaseService == null || logger == null)
            return;

        foreach (var imagePath in imagePaths)
        {
            if (!File.Exists(imagePath))
            {
                logger.LogWarning("Image {ImagePath} does not exist", imagePath);
                continue;
            }

            // Skip duplicates
            bool duplicate = false;
            foreach (var existingMedia in MediaToImport)
            {
                if (existingMedia.MediaToShow is LocalMediaSource localMediaSource)
                {
                    if (localMediaSource.LocalPath == imagePath)
                    {
                        logger.LogInformation("Image {ImagePath} already exists in import list", imagePath);
                        duplicate = true;
                        break;
                    }
                }
            }

            if (duplicate)
                continue;

            logger.LogInformation("Adding image {ImagePath} for import configuring", imagePath);

            var imageSource = new LocalMediaSource(imagePath, serviceProvider!);
            MediaToImport.Add(new MediaViewerViewModel(logger, windowService!)
            {
                ShowingThumbnail = true,
                MediaToShow = imageSource,
                MediaOpenResources = new ShowMediaInSeparateWindow(windowService!),
                Name = Path.GetFileName(imagePath),
                AllowSelection = true,
            });
        }
    }

    public void Dispose()
    {
        foreach (var media in MediaToImport)
        {
            media.Dispose();
        }

        Hamburger.Dispose();
    }

    private async Task PerformImport(List<MediaViewerViewModel> media)
    {
        if (clientDatabaseService == null || windowService == null)
            return;

        var target = TargetImportPath.Trim();
        var secondary = ExtraPathToAddTo.Trim();
        var datasetName = (DatasetForAutoIncludeName ?? string.Empty).Trim();

        if (secondary == target)
            secondary = string.Empty;

        // Preload dataset sidecar data if requested
        string commonPrefix = string.Empty;
        Dictionary<string, string> sidecarTagsByPath = new();

        try
        {
            // Check first if the folder exists from the backend
            var folder = await clientDatabaseService.GetMediaFolderFromPathAsync(target);

            if (folder == null)
            {
                // If not, ask if the user wants to create more
                var result = await windowService.ShowConfirmationWindow("Create Folder?",
                    $"The folder '{target}' does not exist, do you want to create it?");

                if (result != true)
                {
                    Dispatcher.UIThread.Post(() => { StatusText = "Canceled folder creation"; });
                    return;
                }

                // The import process automatically creates the target folder
            }

            if (!string.IsNullOrWhiteSpace(secondary))
            {
                folder = await clientDatabaseService.GetMediaFolderFromPathAsync(secondary);

                if (folder == null)
                {
                    // If not, ask if the user wants to create more
                    var result = await windowService.ShowConfirmationWindow("Create Secondary Folder?",
                        $"The folder '{secondary}' does not exist, do you want to create it?");

                    if (result != true)
                    {
                        Dispatcher.UIThread.Post(() => { StatusText = "Canceled folder creation"; });
                        return;
                    }
                }
            }

            // And then can one by one request to the backend that it imports an image
            foreach (var thing in media)
            {
                var local = thing.MediaToShow as LocalMediaSource;

                if (local == null)
                {
                    logger?.LogWarning("Media  is not a local media source, skipping");
                    continue;
                }

                logger?.LogInformation("Importing {ImagePath} to {Target}", local.LocalPath, target);

                var mediaConfiguration = await backendAPI!.ImportMedia(Path.GetFileName(local.LocalPath),
                    File.OpenRead(local.LocalPath), target, ImportAlphaAsMask);

                if (!string.IsNullOrWhiteSpace(secondary))
                {
                    logger?.LogInformation("Adding {ImagePath} to {Secondary}", local.LocalPath, secondary);
                    await clientDatabaseService.AddMediaToFolder(mediaConfiguration.Id, secondary);
                }

                if (DeleteAfterImport)
                {
                    logger?.LogInformation("Deleting {ImagePath}", local.LocalPath);
                    File.Delete(local.LocalPath);
                }

                Dispatcher.UIThread.Post(() => { OnMediaImported(thing); });
            }

            Dispatcher.UIThread.Post(() => { StatusText = "Done"; });
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to import media");
            Dispatcher.UIThread.Post(() => { StatusText = "Failed to import: " + e.Message; });
        }
    }

    private static string ComputeCommonPrefix(List<string> texts)
    {
        if (texts.Count == 0)
            return string.Empty;

        var prefix = texts[0];
        foreach (var text in texts.Skip(1))
        {
            var current = text;
            var max = Math.Min(prefix.Length, current.Length);
            int i = 0;
            while (i < max && prefix[i] == current[i])
                ++i;
            prefix = prefix[..i];
            if (prefix.Length == 0)
                break;
        }

        return prefix;
    }

    private static string RemoveCommonPrefix(string text, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return text;

        if (text.StartsWith(prefix, StringComparison.Ordinal))
            return text[prefix.Length..].Trim();

        return text;
    }

    private static string TrimTagBoundaries(string text)
    {
        // Trim spaces, newlines, and leading/trailing commas which are common in tag lists
        return text.Trim().Trim(' ', '\t', '\r', '\n', ',');
    }

    private void OnMediaImported(MediaViewerViewModel media)
    {
        // Remove from display
        if (MediaToImport.Remove(media))
            media.Dispose();
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Import", Command = new RelayCommand(StartImport) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Open Media Collection", Command = new RelayCommand(OpenMediaWindow) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}
