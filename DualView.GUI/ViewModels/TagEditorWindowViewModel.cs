using System;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public sealed class TagEditorWindowViewModel : ViewModelBase, IDisposable
{
    private Action? onClosed;

    public TagEditorWindowViewModel()
    {
        TagEditor = new TagEditorViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public TagEditorWindowViewModel(TagEditorViewModel tagEditor)
    {
        TagEditor = tagEditor;
    }

    public TagEditorViewModel TagEditor { get; }

    public string Title
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Edit Tags";

    public void InitializeMedia(long mediaId, Action closeCallback)
    {
        Title = "Edit Media Tags";
        onClosed = closeCallback;
        TagEditor.ConfigureMedia([mediaId]);
    }

    public void InitializeCollection(long collectionId, Action closeCallback)
    {
        Title = "Edit Collection Tags";
        onClosed = closeCallback;
        TagEditor.ConfigureCollections([collectionId]);
    }

    public void Dispose()
    {
        var callback = onClosed;
        onClosed = null;
        callback?.Invoke();
    }
}
