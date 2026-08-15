using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DualView.GUI.ViewModels;

public sealed class ImportSectionViewModel : ViewModelBase, IDisposable
{
    private readonly IClientDatabaseService databaseService;
    private readonly ILogger? logger;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;
    private readonly long id;

    // Preview constructor
    public ImportSectionViewModel()
    {
        databaseService = null!;
        Name = "Test name";

        FolderPicker = new FolderPickerViewModel();
        RemoveAfterImport = true;
        id = -1;
        IsActive = true;
        TargetFolderId = 1;
    }

    [ActivatorUtilitiesConstructor]
    public ImportSectionViewModel(UploadSectionDTO section, IClientDatabaseService databaseService,
        ILogger? logger, IWindowService? windowService, ILogger<FolderPickerViewModel>? folderPickerLogger,
        IServiceProvider? serviceProvider)
    {
        this.databaseService = databaseService;
        this.logger = logger;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;

        id = section.Id;
        Name = section.Name;
        KeepEvenWhenEmpty = section.KeepTarget;
        RemoveAfterImport = section.RemoveAfterImport;
        IsActive = section.Selected;
        TargetFolderId = section.TargetFolderId;

        // If one service is given, assume all are available
        FolderPicker = folderPickerLogger != null && windowService != null
            ? new FolderPickerViewModel(folderPickerLogger, databaseService, windowService, serviceProvider!)
            : new FolderPickerViewModel();
        FolderPicker.PropertyChanged += OnFolderPickerPropertyChanged;
        _ = InitializeFolderPathAsync(section.TargetFolderId);

        foreach (var media in section.Media)
        {
            var viewer = new MediaViewerViewModel(logger ?? NullLogger.Instance, windowService!)
            {
                Name = media.OriginalFileName,
                MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(media),
                    serviceProvider ?? Program.ServiceProvider!),
                ShowingThumbnail = true,
                AllowSelection = true,
                Selected = false,
            };
            Media.Add(viewer);
            viewer.OnSelectionChanged += OnMediaSelectionChanged;
        }
    }

    public ObservableCollection<MediaViewerViewModel> Media { get; } = new();
    public FolderPickerViewModel FolderPicker { get; }

    public bool CollectionTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ImagesTabSelected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsCollapsed
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Name
    {
        get;
        set => SetProperty(ref field, value);
    }

    public long TargetFolderId { get; set; }

    public bool KeepEvenWhenEmpty
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool RemoveAfterImport
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool IsActive
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int ImageCount => Media.Count;
    public int SelectedCount => Media.Count(item => item.Selected);

    public void SelectCollectionTab()
    {
        CollectionTabSelected = true;
        ImagesTabSelected = false;
        IsCollapsed = false;
    }

    public void SelectImagesTab()
    {
        CollectionTabSelected = false;
        ImagesTabSelected = true;
        IsCollapsed = false;
    }

    public void Collapse()
    {
        CollectionTabSelected = false;
        ImagesTabSelected = false;
        IsCollapsed = true;
    }

    public async Task SetActiveAsync()
    {
        await databaseService.SetUploadSectionActiveAsync(IsActive ? id : null);
    }

    // TODO: this should automatically save any changes after like a second (or if import is pressed immediately before doing the import)
    // This is probably needed just for the name change text box
    public async Task SaveAsync()
    {
        await databaseService.SaveUploadSectionAsync(new UploadSectionDTO
        {
            Id = id, Name = Name.Trim(), KeepTarget = KeepEvenWhenEmpty, RemoveAfterImport = RemoveAfterImport,
            TargetFolderId = TargetFolderId,
        });
    }

    public async Task ImportAsync()
    {
        var selected = Media.Where(item => item.Selected)
            .Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList();
        await databaseService.ImportUploadSectionAsync(id, selected.Count == 0 ? null : selected);

        // TODO: we need a signal R message to update the UI
    }

    public async Task RemoveSelectedAsync()
    {
        var selected = Media.Where(item => item.Selected).ToList();
        await databaseService.RemoveMediaFromUploadSectionAsync(id,
            selected.Select(item => ((ServerMediaSource)item.MediaToShow!).ServerId).ToList());
        foreach (var item in selected)
        {
            item.OnSelectionChanged -= OnMediaSelectionChanged;
            item.Dispose();
            Media.Remove(item);
        }

        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void Dispose()
    {
        foreach (var media in Media)
        {
            media.OnSelectionChanged -= OnMediaSelectionChanged;
            media.Dispose();
        }

        FolderPicker.PropertyChanged -= OnFolderPickerPropertyChanged;
        FolderPicker.Dispose();
    }

    private async Task InitializeFolderPathAsync(long folderId)
    {
        if (folderId == MediaFolderInfo.RootFolderId)
        {
            FolderPicker.SelectedPath = "/";
            return;
        }

        try
        {
            FolderPicker.SelectedPath = await databaseService.GetMediaFolderPath(folderId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to resolve import target folder {FolderId}", folderId);
        }
    }

    private async void OnFolderPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderPickerViewModel.SelectedPath))
            return;

        var folder = await databaseService.GetMediaFolderFromPathAsync(FolderPicker.SelectedPath);
        if (folder != null)
        {
            TargetFolderId = folder.Id;
            OnPropertyChanged(nameof(TargetFolderId));

            // TODO: trigger backend save immediately
        }
    }

    private void OnMediaSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedCount));
    }
}
