using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace DualView.GUI.Services;

public interface IWindowRecoveryService
{
    public void RegisterMediaViewer(Window window, long mediaId, long? collectionId);

    public void UpdateMediaViewer(Window window, long mediaId);
    public void RegisterCollectionWindow(Window window, long collectionId);
    public void RegisterSingletonWindow(Window window, Type viewModelType);
    public void UnregisterWindow(Window window);
    public Task RecoverWindowsAsync(IWindowService windowService);
    public void Clear();
}
