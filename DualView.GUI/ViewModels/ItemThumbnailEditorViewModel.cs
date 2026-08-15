using System;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class ItemThumbnailEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;
    private readonly IServiceProvider? serviceProvider;

    private GeneratedMediaSource source;
    private long sourceId;

    private ConfiguredMediaDTO? currentlyShownThumbnail;

    public event Action? OnEdited;

    // Design time constructor
    public ItemThumbnailEditorViewModel()
    {
        ThumbnailViewer = new MediaViewerViewModel
        {
            ShowingThumbnail = true,
        };
    }

    [ActivatorUtilitiesConstructor]
    public ItemThumbnailEditorViewModel(ILogger logger, IWindowService windowService,
        IClientDatabaseService clientDatabaseService, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
        this.serviceProvider = serviceProvider;

        ThumbnailViewer = new MediaViewerViewModel(logger, windowService)
        {
            ShowingThumbnail = true,
            Name = "No thumbnail",
        };
    }

    public MediaViewerViewModel ThumbnailViewer { get; }

    public IItemWithThumbnail? EditedItem
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);

            if (EditedItem == null)
            {
                currentlyShownThumbnail = null;
                RefreshShownThumbnail();
            }
            else
            {
                if (EditedItem.Thumbnail != currentlyShownThumbnail)
                {
                    currentlyShownThumbnail = EditedItem.Thumbnail != null ? new ConfiguredMediaDTO(EditedItem.Thumbnail) : null;
                    RefreshShownThumbnail();
                }
            }
        }
    }

    private void RefreshShownThumbnail()
    {
        if (currentlyShownThumbnail == null)
        {
            ThumbnailViewer.MediaToShow = null;
            ThumbnailViewer.MediaOpenResources = null;
            ThumbnailViewer.Name = "No thumbnail";
            return;
        }

        ThumbnailViewer.MediaToShow = new ServerMediaSource(currentlyShownThumbnail, serviceProvider!);
        ThumbnailViewer.MediaOpenResources =
            new ShowMediaInSeparateWindow(
                windowService ?? throw new Exception("Window service not set"), null);
        ThumbnailViewer.Name = currentlyShownThumbnail.Name;
    }

    public void Setup(GeneratedMediaSource mediaSource, long mediaSourceId)
    {
        source = mediaSource;
        sourceId = mediaSourceId;
    }

    public void ViewGeneratedMedia()
    {
        if (EditedItem == null)
            return;

        windowService?.ShowGeneratedMediaView(source, sourceId, OnMediaSelected);
    }

    public void ViewAllMedia()
    {
        if (EditedItem == null || windowService == null)
            return;
        windowService.ShowNoticeWindow("Media selection is not available yet.");
    }

    private void OnMediaSelected(IVisualMediaSource resource)
    {
        if (EditedItem == null || clientDatabaseService == null)
            return;

        if (resource is ServerMediaSource serverMediaSource)
        {
            if (currentlyShownThumbnail == null || currentlyShownThumbnail.Id != serverMediaSource.ServerId)
            {
                // We need to fetch the new data and set it
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var thumbnail = await clientDatabaseService.GetConfiguredMediaAsync(serverMediaSource.ServerId);

                        if (thumbnail == null)
                        {
                            logger?.LogError("New selected thumbnail not found with id: {Id}",
                                serverMediaSource.ServerId);
                            return;
                        }

                        logger?.LogInformation("Changing item thumbnail to: {Id}", thumbnail.Id);

                        Dispatcher.UIThread.Post(() =>
                        {
                            // Notify our parent about the change
                            EditedItem.Thumbnail = thumbnail;
                            OnEdited?.Invoke();

                            // Refresh our view
                            currentlyShownThumbnail = new ConfiguredMediaDTO(thumbnail);
                            RefreshShownThumbnail();
                        });
                    }
                    catch (Exception e)
                    {
                        windowService?.ShowErrorWindow("Failed to load media", e);
                    }
                });
            }
        }
        else
        {
            logger?.LogWarning("Unexpected media type set: {Resource}", resource);
        }
    }

    public void Dispose()
    {
        ThumbnailViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
