using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;

namespace DualView.GUI.ViewModels;

public enum ExportFormat
{
    IndividualFiles,
    Zip,
}

public enum ExportNameMode
{
    OriginalName,
    MediaId,
}

public enum ExportOrder
{
    CollectionOrder,
    CreatedTime,
    LastViewedTime,
    Name,
    Type,
}

public sealed class ExportSetupWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IClientDatabaseService? databaseService;
    private readonly IWindowService? windowService;
    private CancellationTokenSource? exportCancellation;
    private CollectionDTO? collection;
    private HashSet<long>? selectedIds;

    public ExportSetupWindowViewModel()
    {
        InitializeOptions();
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ExportSetupWindowViewModel(IClientDatabaseService databaseService, IWindowService windowService)
    {
        this.databaseService = databaseService;
        this.windowService = windowService;
        InitializeOptions();
    }

    // These are used by AXAML.
    // ReSharper disable CollectionNeverQueried.Global
    public ObservableCollection<ExportFormat> FormatOptions { get; } = new();

    public ObservableCollection<ExportNameMode> NameModeOptions { get; } = new();
    public ObservableCollection<ExportOrder> OrderOptions { get; } = new();
    // ReSharper restore CollectionNeverQueried.Global

    public string Title => $"Export - {collection?.Name ?? "Collection"}";

    public string ExportPath
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public ExportFormat Format
    {
        get;
        set => SetProperty(ref field, value);
    } = ExportFormat.IndividualFiles;

    public bool CreateCollectionSubfolder
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public ExportNameMode NameMode
    {
        get;
        set => SetProperty(ref field, value);
    } = ExportNameMode.OriginalName;

    public bool PrefixCollectionOrder
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ExportOrder Order
    {
        get;
        set => SetProperty(ref field, value);
    } = ExportOrder.CollectionOrder;

    public bool ReverseOrder
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int SkipCount
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    public int ExportLimit
    {
        get;
        set => SetProperty(ref field, Math.Max(1, value));
    } = 10000;

    public bool IsExporting => exportCancellation != null;

    public double Progress
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string ProgressText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    public event EventHandler? CloseRequested;
    public event Func<Task<string?>>? FolderPickerRequested;

    public void Initialize(CollectionDTO collectionDTO, HashSet<long>? mediaFileIds)
    {
        collection = collectionDTO;
        selectedIds = mediaFileIds;
        OnPropertyChanged(nameof(Title));
    }

    public async void BrowseExportPath()
    {
        if (FolderPickerRequested != null)
        {
            var path = await FolderPickerRequested.Invoke();
            if (path != null)
                ExportPath = path;
        }
    }

    public void Cancel()
    {
        if (exportCancellation == null)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            exportCancellation.Cancel();
        }
    }

    public void Export() => _ = ExportAsync();

    public void Dispose()
    {
        exportCancellation?.Cancel();
        exportCancellation?.Dispose();
    }

    private void InitializeOptions()
    {
        FormatOptions.Add(ExportFormat.IndividualFiles);
        FormatOptions.Add(ExportFormat.Zip);
        NameModeOptions.Add(ExportNameMode.OriginalName);
        NameModeOptions.Add(ExportNameMode.MediaId);
        OrderOptions.Add(ExportOrder.CollectionOrder);
        OrderOptions.Add(ExportOrder.CreatedTime);
        OrderOptions.Add(ExportOrder.LastViewedTime);
        OrderOptions.Add(ExportOrder.Name);
        OrderOptions.Add(ExportOrder.Type);
    }

    private async Task ExportAsync()
    {
        if (exportCancellation != null)
            return;
        if (Format == ExportFormat.Zip)
            return;
        if (databaseService == null || collection == null || string.IsNullOrWhiteSpace(ExportPath))
            return;

        var cancellation = new CancellationTokenSource();
        exportCancellation = cancellation;
        OnPropertyChanged(nameof(IsExporting));

        try
        {
            var media = await databaseService.GetCollectionContents(collection.Id);
            var collectionOrder = media
                .Select((item, index) => (item.Id, Order: index + 1))
                .ToDictionary(item => item.Id, item => item.Order);
            if (selectedIds != null)
                media = media.Where(item => selectedIds.Contains(item.Id)).ToList();

            media = OrderMedia(media);
            media = media.Skip(SkipCount).Take(ExportLimit).ToList();
            var targetDirectory = ExportPath;
            if (CreateCollectionSubfolder)
                targetDirectory = Path.Combine(targetDirectory, MakeSafeName(collection.Name));
            Directory.CreateDirectory(targetDirectory);

            for (var index = 0; index < media.Count; ++index)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var item = media[index];
                var fileName = GetExportFileName(item, collectionOrder);
                var targetPath = Path.Combine(targetDirectory, fileName);
                ProgressText = $"Saving {fileName}";
                Progress = index / (double)Math.Max(1, media.Count);
                await ServerMediaSource.DownloadFullMediaToLocalFile(item.Id, targetPath);
                Progress = (index + 1) / (double)Math.Max(1, media.Count);
            }

            ProgressText = $"Exported {media.Count} file(s).";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Export canceled.";
        }
        catch (Exception ex)
        {
            windowService?.ShowErrorWindow("Failed to export collection", ex);
        }
        finally
        {
            exportCancellation.Dispose();
            exportCancellation = null;
            OnPropertyChanged(nameof(IsExporting));
        }
    }

    private List<MediaFileDTO> OrderMedia(List<MediaFileDTO> media)
    {
        IEnumerable<MediaFileDTO> ordered = Order switch
        {
            ExportOrder.CreatedTime => media.OrderBy(item => item.ImportedAt),
            ExportOrder.LastViewedTime => media.OrderBy(item => item.LastViewed),
            ExportOrder.Name => media.OrderBy(item => item.OriginalFileName, StringComparer.OrdinalIgnoreCase),
            ExportOrder.Type => media.OrderBy(item => item.MediaType).ThenBy(item => item.OriginalFileName),
            _ => media,
        };
        var result = ordered.ToList();
        if (ReverseOrder)
            result.Reverse();
        return result;
    }

    private string GetExportFileName(MediaFileDTO item, IReadOnlyDictionary<long, int> collectionOrder)
    {
        var extension = Path.GetExtension(item.OriginalFileName);
        var name = NameMode == ExportNameMode.MediaId
            ? item.Id.ToString()
            : Path.GetFileNameWithoutExtension(item.OriginalFileName);
        if (PrefixCollectionOrder)
        {
            // name = $"{collectionOrder[item.Id]:00000} - {name}";
            name = $"{collectionOrder[item.Id]:00000}_{name}";
        }

        return MakeSafeName(name) + extension;
    }

    private static string MakeSafeName(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
            name = name.Replace(character, '_');
        return string.IsNullOrWhiteSpace(name) ? "Export" : name;
    }
}
