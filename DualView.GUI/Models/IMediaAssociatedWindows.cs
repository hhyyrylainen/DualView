using System;
using DualView.GUI.Services;

namespace DualView.GUI.Models;

public interface IMediaAssociatedWindows
{
    public enum DoubleClickAction
    {
        None,
        OpenView,
        Activate,
        ViewThumbnail,
    }

    public DoubleClickAction DefaultDoubleClickAction { get; }

    public bool HasViewAction { get; }
    public bool HasThumbnailAction { get; }
    public bool HasEditAction { get; }

    public bool HasMoveToFolderAction { get; }
    public bool HasAddToFolderAction { get; }
    public bool HasManageFoldersAction { get; }

    /// <summary>
    ///   Refresh the available options based on the current media source. This is needed to be called before using
    ///   the above properties to have accurate results as some view types cannot know what is available until the
    ///   media type is known.
    /// </summary>
    /// <param name="mediaSource">Media source to refresh based on</param>
    public void RefreshAvailableOptions(IVisualMediaSource? mediaSource);

    /// <summary>
    ///   Trigger the show action
    /// </summary>
    /// <param name="mediaSource">
    ///   Can be called with null media for certain item types that want to handle viewing even then
    /// </param>
    public void ShowView(IVisualMediaSource? mediaSource);

    public void Activate(IVisualMediaSource mediaSource);

    public void ShowThumbnail(IVisualMediaSource mediaSource);

    public void StartEditAction(IVisualMediaSource mediaSource);

    public void StartMoveAction(IVisualMediaSource mediaSource);
    public void StartAddToFolderAction(IVisualMediaSource mediaSource);
    public void StartManageFoldersAction(IVisualMediaSource mediaSource);
}

public class ShowMediaInSeparateWindow : IMediaAssociatedWindows
{
    private readonly IWindowService windowService;
    private readonly ICollectionBrowse? collectionBrowse;

    public ShowMediaInSeparateWindow(IWindowService windowService, ICollectionBrowse? collectionBrowse)
    {
        this.windowService = windowService;
        this.collectionBrowse = collectionBrowse;
    }

    public IMediaAssociatedWindows.DoubleClickAction DefaultDoubleClickAction =>
        IMediaAssociatedWindows.DoubleClickAction.OpenView;

    public bool HasViewAction => true;
    public bool HasThumbnailAction => false;
    public bool HasEditAction => true;
    public bool HasMoveToFolderAction => false;
    public bool HasAddToFolderAction => false;
    public bool HasManageFoldersAction { get; private set; }

    public void RefreshAvailableOptions(IVisualMediaSource? mediaSource)
    {
        HasManageFoldersAction = mediaSource is ServerMediaSource;
    }

    public void ShowView(IVisualMediaSource? mediaSource)
    {
        if (mediaSource == null)
        {
            windowService.ShowNoticeWindow("No media");
            return;
        }

        // Clone the media to not mess with the already loaded one
        windowService.ShowMediaViewer(mediaSource.Clone(), collectionBrowse);
    }

    public void Activate(IVisualMediaSource mediaSource)
    {
        ShowView(mediaSource);
    }

    public void ShowThumbnail(IVisualMediaSource mediaSource)
    {
        throw new NotSupportedException();
    }

    public void StartEditAction(IVisualMediaSource mediaSource)
    {
        if (mediaSource is ServerMediaSource serverMediaSource)
        {
            windowService.ShowMediaEditSetup(serverMediaSource.ServerId);
            return;
        }

        throw new NotSupportedException();
    }

    public void StartMoveAction(IVisualMediaSource mediaSource)
    {
        throw new NotSupportedException();
    }

    public void StartAddToFolderAction(IVisualMediaSource mediaSource)
    {
        throw new NotSupportedException();
    }

    public void StartManageFoldersAction(IVisualMediaSource mediaSource)
    {
        if (mediaSource is ServerMediaSource serverMediaSource)
        {
            windowService.ShowEditMediaFolders(serverMediaSource.Info);
            return;
        }

        throw new NotSupportedException();
    }
}
