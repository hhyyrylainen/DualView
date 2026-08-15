using System;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.ViewModels;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;

namespace DualView.GUI.Services;

/// <summary>
///   Allows showing windows from anywhere. Note that most of the methods are only allowed from the UI thread.
/// </summary>
public interface IWindowService
{
    /// <summary>
    ///    Shows or brings to front a singleton window for a given ViewModel type.
    /// </summary>
    /// <returns>
    ///   The view model so that calling code can change the state if required. Might be null if a problem occurred.
    /// </returns>
    public TViewModel? ShowSingletonWindow<TViewModel>()
        where TViewModel : class;

    /// <summary>
    ///    Shows a new window for a given ViewModel type.
    /// </summary>
    public void ShowWindow<TViewModel>(Action<TViewModel>? onCreated = null)
        where TViewModel : class;

    /// <summary>
    ///   Shows a popup window with an error message. This can be called at any time from any thread for convenience.
    /// </summary>
    /// <param name="errorTitle">Short description of what went wrong</param>
    /// <param name="exception">Exception with extended info</param>
    public void ShowErrorWindow(string errorTitle, Exception exception);

    /// <summary>
    ///   Shows a non-error popup with a message and OK button
    /// </summary>
    /// <param name="message">Message to show</param>
    /// <param name="customTitle">If set, overrides the default title</param>
    public void ShowNoticeWindow(string message, string? customTitle = null);

    /// <summary>
    ///   Shows a new window for editing text
    /// </summary>
    /// <param name="editable">Thing to edit in it</param>
    public void ShowEditWindow(IEditableText editable);

    /// <summary>
    ///   Shows a popup window asking "yes / no" and if <see cref="allowCancel"/> is true also allows canceling.
    /// </summary>
    /// <param name="title">Title of the window</param>
    /// <param name="question">The question to ask</param>
    /// <param name="allowCancel">When true also shows a cancel button</param>
    /// <returns>True if accepted, false if rejected, and null if canceled</returns>
    public Task<bool?> ShowConfirmationWindow(string title, string question, bool allowCancel = false);

    /// <summary>
    ///   Shows the given media source in a dedicated window
    /// </summary>
    /// <param name="mediaSource">Media to view</param>
    /// <param name="collectionBrowse">Browsing support in a collection</param>
    public void ShowMediaViewer(IVisualMediaSource mediaSource, ICollectionBrowse? collectionBrowse);

    /// <summary>
    ///   Shows a window tracking the progress of a backend operation
    /// </summary>
    /// <param name="operationId">ID of the operation to show status for</param>
    public void ShowOperationStatus(long operationId);

    public void ShowGeneratedMediaView(GeneratedMediaSource source, long sourceId,
        Action<IVisualMediaSource>? onMediaSelected);

    /// <summary>
    ///   Shows a setup screen for editing a media.
    /// </summary>
    public void ShowMediaEditSetup(long mediaConfigurationId);

    /// <summary>
    ///   Shows the media editor for the given media configuration. May not be called with a prime configuration!
    /// </summary>
    public void ShowMediaEditor(long mediaConfigurationId);

    public void ShowEditMediaFolders(long mediaConfigurationId);

    public void ShowEditMediaFolders(IConfiguredMediaInfo item);

    /// <summary>
    ///   Shows the media selection window.
    /// </summary>
    /// <returns>The selected media or null if cancelled</returns>
    public void ShowTextInputWindow(string title, string explanation, string? initialValue,
        Func<TextInputWindowViewModel, Task<bool>> onAccept, string? placeholder = null);
}
