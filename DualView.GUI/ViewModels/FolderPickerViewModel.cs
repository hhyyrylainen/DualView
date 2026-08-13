using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class FolderPickerViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<FolderPickerViewModel>? logger;
    private readonly IClientDatabaseService? clientDatabaseService;
    private readonly IWindowService? windowService;
    private readonly IServiceProvider? serviceProvider;

    private List<MediaFolderInfo> allFolders = new();
    private long currentFolderId = MediaFolderInfo.RootFolderId;
    private CancellationTokenSource? searchDebounceCancellation;

    public FolderPickerViewModel()
    {
        CurrentPath = "/";
    }

    [ActivatorUtilitiesConstructor]
    public FolderPickerViewModel(ILogger<FolderPickerViewModel> logger,
        IClientDatabaseService clientDatabaseService, IWindowService windowService, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.clientDatabaseService = clientDatabaseService;
        this.windowService = windowService;
        this.serviceProvider = serviceProvider;
        _ = Refresh();
    }

    public ObservableCollection<MediaViewerViewModel> Folders { get; } = new();

    public string CurrentPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "/";

    public string SelectedPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "/";

    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                ScheduleSearchRefresh();
        }
    } = string.Empty;

    public Func<string, Task>? RequestCopyToClipboard { get; set; }
    public Func<Task<string>>? RequestPasteFromClipboard { get; set; }

    public void NavigateUp()
    {
        if (CurrentPath == "/")
            return;

        var separator = CurrentPath.LastIndexOf('/');
        NavigateToPath(separator <= 0 ? "/" : CurrentPath[..separator]);
    }

    public void NavigateToEnteredPath()
    {
        _ = NavigateToPathAsync(CurrentPath);
    }

    public void CopyPath()
    {
        if (RequestCopyToClipboard == null)
            return;

        _ = CopyPathAsync();
    }

    public void PastePath()
    {
        if (RequestPasteFromClipboard == null)
            return;

        _ = PastePathAsync();
    }

    public void CreateNewFolder()
    {
        if (windowService == null)
            return;

        windowService.ShowTextInputWindow("New Media Folder", "Enter a name for the new folder:", null,
            input => CreateFolderAsync(input.Input, input));
    }

    public void NavigateIntoFolder(long folderId)
    {
        var folder = allFolders.FirstOrDefault(item => item.Id == folderId);
        if (folder == null)
            return;

        NavigateToPath(CurrentPath == "/" ? $"/{folder.Name}" : $"{CurrentPath}/{folder.Name}");
    }

    public void ManageFolder(long folderId)
    {
        var folder = allFolders.FirstOrDefault(item => item.Id == folderId);
        if (folder == null)
            return;

        windowService?.ShowEditMediaFolders(new ConfiguredMediaInfo(folder.Name, folder.Id, folder.Id,
            MediaType.Png, 0, 0) { IsFolder = true });
    }

    public void Dispose()
    {
        foreach (var folder in Folders)
            folder.Dispose();

        Folders.Clear();
        searchDebounceCancellation?.Cancel();
        searchDebounceCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task Refresh()
    {
        if (clientDatabaseService == null)
            return;

        try
        {
            allFolders = await LoadAllFoldersAsync();
            await NavigateToPathAsync(CurrentPath);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to load folders for folder picker");
            windowService?.ShowErrorWindow("Failed to load folders", e);
        }
    }

    private async Task NavigateToPathAsync(string? path)
    {
        if (clientDatabaseService == null)
            return;

        path = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        path = path.TrimEnd('/');
        if (path.Length == 0)
            path = "/";

        var folder = path == "/" ? null : await clientDatabaseService.GetMediaFolderFromPathAsync(path);
        if (path != "/" && folder == null)
        {
            windowService?.ShowNoticeWindow("The entered folder path does not exist");
            return;
        }

        currentFolderId = folder?.Id ?? MediaFolderInfo.RootFolderId;
        CurrentPath = path;
        SelectedPath = path;

        var children = allFolders.Where(item => currentFolderId == MediaFolderInfo.RootFolderId
                ? item.ParentIds.Contains(MediaFolderInfo.RootFolderId)
                : item.ParentIds.Contains(currentFolderId))
            .Where(item => string.IsNullOrWhiteSpace(SearchText) ||
                           item.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var existing in Folders)
                existing.Dispose();

            Folders.Clear();
            foreach (var child in children)
            {
                Folders.Add(new MediaViewerViewModel(logger!, windowService!)
                {
                    Name = child.Name,
                    MediaToShow = new ServerMediaSource(new ConfiguredMediaInfo(child.Name, child.Id, child.Id,
                        MediaType.Png, 0, 0) { IsFolder = true }, serviceProvider!),
                    MediaOpenResources = new FolderPickerMediaActions(this, child.Id),
                    ShowingThumbnail = true,
                });
            }
        });
    }

    private void NavigateToPath(string path)
    {
        _ = NavigateToPathAsync(path);
    }

    private void ScheduleSearchRefresh()
    {
        searchDebounceCancellation?.Cancel();
        searchDebounceCancellation?.Dispose();
        searchDebounceCancellation = new CancellationTokenSource();
        var cancellationToken = searchDebounceCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(333, cancellationToken);
                await NavigateToPathAsync(CurrentPath);
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private async Task<List<MediaFolderInfo>> LoadAllFoldersAsync()
    {
        var folders = new List<MediaFolderInfo>();
        var pendingParents = new Queue<long?>();
        pendingParents.Enqueue(null);

        while (pendingParents.Count > 0)
        {
            var parentId = pendingParents.Dequeue();
            var children = await clientDatabaseService!.GetMediaFoldersAsync(parentId);
            foreach (var child in children)
            {
                if (child.Id == MediaFolderInfo.RootFolderId || folders.Any(folder => folder.Id == child.Id))
                    continue;

                folders.Add(child);
                pendingParents.Enqueue(child.Id);
            }
        }

        return folders;
    }

    private async Task CopyPathAsync()
    {
        try
        {
            await RequestCopyToClipboard!.Invoke(SelectedPath);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to copy folder path");
        }
    }

    private async Task PastePathAsync()
    {
        try
        {
            var path = await RequestPasteFromClipboard!.Invoke();
            await NavigateToPathAsync(path);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Failed to paste folder path");
        }
    }

    private async Task<bool> CreateFolderAsync(string? name, TextInputWindowViewModel input)
    {
        if (clientDatabaseService == null || string.IsNullOrWhiteSpace(name))
        {
            input.Error = "Folder name cannot be empty";
            return false;
        }

        try
        {
            await clientDatabaseService.CreateMediaFolder(name.Trim(), currentFolderId);
            await Refresh();
            return true;
        }
        catch (Exception e)
        {
            input.Error = $"Failed to create folder: {e.Message}";
            return false;
        }
    }

    private sealed class FolderPickerMediaActions(FolderPickerViewModel picker, long folderId) : IMediaAssociatedWindows
    {
        public IMediaAssociatedWindows.DoubleClickAction DefaultDoubleClickAction =>
            IMediaAssociatedWindows.DoubleClickAction.OpenView;

        public bool HasViewAction => true;
        public bool HasThumbnailAction => false;
        public bool HasEditAction => false;
        public bool HasMoveToFolderAction => false;
        public bool HasAddToFolderAction => false;
        public bool HasManageFoldersAction => true;

        public void RefreshAvailableOptions(IVisualMediaSource? mediaSource)
        {
        }

        public void ShowView(IVisualMediaSource? mediaSource) => picker.NavigateIntoFolder(folderId);
        public void ShowThumbnail(IVisualMediaSource mediaSource) => picker.NavigateIntoFolder(folderId);

        public void StartEditAction(IVisualMediaSource mediaSource)
        {
        }

        public void StartMoveAction(IVisualMediaSource mediaSource)
        {
        }

        public void StartAddToFolderAction(IVisualMediaSource mediaSource)
        {
        }

        public void StartManageFoldersAction(IVisualMediaSource mediaSource) => picker.ManageFolder(folderId);
    }
}
