using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using DualView.GUI.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public sealed class HamburgerMenuItem
{
    public string Title { get; init; } = "";
    public ICommand? Command { get; init; }
    public object? Parameter { get; init; }
}

public sealed class HamburgerMenuViewModel : ViewModelBase, IDisposable
{
    private readonly IBackendStatusService? backendStatusService;
    private Action<bool>? refreshStatus;

    /// <summary>
    ///   View model type that doesn't show backed status. For use in previews.
    /// </summary>
    public HamburgerMenuViewModel()
    {
        IsConnected = true;
    }

    [ActivatorUtilitiesConstructor]
    public HamburgerMenuViewModel(IBackendStatusService backendStatusService)
    {
        this.backendStatusService = backendStatusService;

        refreshStatus = RefreshStatus;
        backendStatusService.OnStatusChanged += refreshStatus;

        // Refresh status immediately
        IsConnected = backendStatusService.IsConnected;
        SetToolTipText();

        RefreshStatus(backendStatusService.IsConnected);
    }

    public ObservableCollection<HamburgerMenuItem> MenuItems { get; } = new();

    public bool IsConnected
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string ToolTipText
    {
        get;
        set => SetProperty(ref field, value);
    } = "Contacting backend...";

    public void Dispose()
    {
        if (backendStatusService != null)
        {
            backendStatusService.OnStatusChanged -= refreshStatus;
            refreshStatus = null;
        }
    }

    private void RefreshStatus(bool backendConnected)
    {
        if (IsConnected == backendConnected)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = backendConnected;
            SetToolTipText();
        });
    }

    private void SetToolTipText()
    {
        ToolTipText = IsConnected ? "Open Menu" : "Connection to backend lost! Most functions are unavailable.";
    }
}
