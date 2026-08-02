using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class UploadWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<UploadWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IBackendAPI? backendAPI;
    private readonly IServiceProvider? serviceProvider;

    public UploadWindowViewModel()
    {
        // Design time
        FilesToUpload.Add(new UploadFileEntry("image.png", null));
    }

    [ActivatorUtilitiesConstructor]
    public UploadWindowViewModel(ILogger<UploadWindowViewModel> logger,
        IWindowService windowService, IBackendAPI backendAPI, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.backendAPI = backendAPI;
        this.serviceProvider = serviceProvider;
    }

    public ObservableCollection<UploadFileEntry> FilesToUpload { get; } = new();

    public bool IsUploading
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool DeleteAfterUpload
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool RemoveAfterUpload
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public string UploadSectionName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public double TotalProgress
    {
        get;
        set => SetProperty(ref field, value);
    }

    public void AddFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            if (FilesToUpload.Any(f => f.LocalPath == path)) continue;
            FilesToUpload.Add(new UploadFileEntry(path, serviceProvider));
        }
    }

    public void RemoveSelectedFiles()
    {
        foreach (var file in FilesToUpload.Where(f => f.IsSelected).ToList())
        {
            file.Dispose();
            FilesToUpload.Remove(file);
        }
    }

    public void RemoveAll()
    {
        foreach (var file in FilesToUpload)
            file.Dispose();

        FilesToUpload.Clear();
    }

    public void SelectAll()
    {
        foreach (var file in FilesToUpload)
            file.IsSelected = true;
    }

    public async Task StartUpload()
    {
        if (IsUploading || FilesToUpload.Count == 0 || backendAPI == null) return;

        var toUpload = FilesToUpload.Where(f => f.IsSelected && f.Status == "Pending").ToList();

        if (toUpload.Count == 0) return;

        IsUploading = true;
        TotalProgress = 0;

        try
        {
            int uploadedCount = 0;
            foreach (var file in toUpload)
            {
                file.Status = "Uploading...";

                try
                {
                    await using var stream = File.OpenRead(file.LocalPath);
                    // Use ImportMedia for now, but we might want a specific upload endpoint
                    await backendAPI.ImportMedia(Path.GetFileName(file.LocalPath), stream, UploadSectionName,
                        file.LocalPath);
                    file.Status = "Done";
                }
                catch (Exception e)
                {
                    file.Status = $"Error: {e.Message}";
                    logger?.LogError(e, "Failed to upload file {File}", file.LocalPath);
                }

                uploadedCount++;
                TotalProgress = (double)uploadedCount / toUpload.Count;
            }
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Upload failed", e);
        }
        finally
        {
            if (DeleteAfterUpload)
            {
                foreach (var file in FilesToUpload.Where(f => f.Status == "Done").ToList())
                {
                    try
                    {
                        File.Delete(file.LocalPath);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            file.Dispose();
                            FilesToUpload.Remove(file);
                        });
                    }
                    catch (Exception e)
                    {
                        windowService?.ShowErrorWindow("Failed to delete file", e);
                    }
                }
            }
            else if (RemoveAfterUpload)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var file in FilesToUpload.Where(f => f.Status == "Done").ToList())
                    {
                        file.Dispose();
                        FilesToUpload.Remove(file);
                    }
                });
            }

            IsUploading = false;
        }
    }

    public void Dispose()
    {
        foreach (var file in FilesToUpload)
            file.Dispose();

        FilesToUpload.Clear();
    }

    public class UploadFileEntry : ViewModelBase, IDisposable
    {
        public UploadFileEntry(string path, IServiceProvider? serviceProvider)
        {
            LocalPath = path;
            FileName = Path.GetFileName(path);

            if (serviceProvider != null)
            {
                ThumbnailViewer = ActivatorUtilities.CreateInstance<MediaViewerViewModel>(serviceProvider);
                ThumbnailViewer.MediaToShow = new LocalMediaSource(path, serviceProvider);
                ThumbnailViewer.IsVisible = true;
                ThumbnailViewer.AllowSelection = true;
                ThumbnailViewer.Name = FileName;
                ThumbnailViewer.Selected = IsSelected;
                ThumbnailViewer.OnSelectionChanged += (s, e) => IsSelected = ThumbnailViewer.Selected;
            }
            else
            {
                // Design time
                ThumbnailViewer = new MediaViewerViewModel();
                ThumbnailViewer.Name = FileName;
            }
        }

        public string LocalPath { get; }
        public string FileName { get; }

        public bool IsSelected
        {
            get;
            set
            {
                if (SetProperty(ref field, value) && ThumbnailViewer != null)
                {
                    ThumbnailViewer.Selected = value;
                }
            }
        } = true;

        public string Status
        {
            get;
            set => SetProperty(ref field, value);
        } = "Pending";

        public MediaViewerViewModel ThumbnailViewer { get; }

        public void Dispose()
        {
            ThumbnailViewer.Dispose();
        }
    }
}
