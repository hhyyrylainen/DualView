using System;
using DualView.GUI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public class AboutWindowViewModel : ViewModelBase, IDisposable
{
    // Design time constructor
    public AboutWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();

        InitializeMenu();
    }

    [ActivatorUtilitiesConstructor]
    public AboutWindowViewModel(IBackendStatusService backendStatusService)
    {
        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();
    }

    public HamburgerMenuViewModel Hamburger { get; }

    public void Dispose()
    {
        Hamburger.Dispose();
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        // No need to show stuff to open itself, so we pass null
        MainWindowViewModel.AddTrailingMenuItems(Hamburger, null);
    }
}
