using System;
using System.Threading.Tasks;

namespace DualView.GUI.Services;

public interface IBackendStatusService
{
    /// <summary>
    ///   Raised when backend status changes. True is backend connected, false if we are disconnected.
    /// </summary>
    public event Action<bool>? OnStatusChanged;

    public bool IsConnected { get; }

    /// <summary>
    ///   Called once on startup to start the backend connection.
    /// </summary>
    public Task StartBackendConnectionAsync();


    /// <summary>
    ///   Monitors the backend connection and raises the status-changed event.
    /// </summary>
    public void BeginMonitoring();
}
