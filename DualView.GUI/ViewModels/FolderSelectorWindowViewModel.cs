using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.Services;
using DualView.Shared.Models;
using DualView.Shared.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class FolderSelectorWindowViewModel : ViewModelBase
{
    private readonly ILogger<FolderSelectorWindowViewModel>? logger;
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;

    public delegate void OnFolderSelectedHandler(long folderId, FolderType type);

    public event OnFolderSelectedHandler? OnFolderSelected;

    public enum FolderType
    {
        MediaFolder,
    }

    // Design time constructor
    public FolderSelectorWindowViewModel()
    {
        Hamburger = new HamburgerMenuViewModel();
    }

    [ActivatorUtilitiesConstructor]
    public FolderSelectorWindowViewModel(ILogger<FolderSelectorWindowViewModel> logger,
        IWindowService windowService, IClientDatabaseService clientDatabaseService,
        IBackendStatusService backendStatusService, FolderType selectedType)
    {
        SelectedType = selectedType;
        this.logger = logger;
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);

        InitializeMenu();

        ReloadFolderTree();
    }

    public FolderType SelectedType { get; }

    public long? LastSelectedFolderId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ObservableCollection<FolderTreeNode> Folders { get; } = new();

    public FolderTreeNode? SelectedFolderNode
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);

            if (value != null)
            {
                LastSelectedFolderId = value.ServerId;
            }
        }
    }

    public HamburgerMenuViewModel Hamburger { get; }

    public bool WantsToClose
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    public void ReloadFolderTree()
    {
        _ = LoadFolders();
    }

    public void ConfirmSelection()
    {
        if (LastSelectedFolderId == null)
        {
            windowService?.ShowNoticeWindow("No folder selected");
            return;
        }

        logger?.LogInformation("Confirming selection of folder {Id}", LastSelectedFolderId.Value);
        if (OnFolderSelected != null)
        {
            try
            {
                OnFolderSelected.Invoke(LastSelectedFolderId.Value, SelectedType);
            }
            catch (Exception e)
            {
                windowService?.ShowErrorWindow("Failed to select folder", e);
                return;
            }

            WantsToClose = true;
        }
    }

    public void Cancel()
    {
        logger?.LogInformation("Canceling folder selection");

        WantsToClose = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hamburger.Dispose();
        }
    }

    private async Task LoadFolders()
    {
        if (clientDatabaseService == null)
            return;

        try
        {
            // As the tree doesn't really support getting child content when needed, we have to load *everything* into
            // memory at once
            List<IFolderInfo> folders;
            switch (SelectedType)
            {
                case FolderType.MediaFolder:
                    folders = (await clientDatabaseService.GetMediaFoldersAsync()).Select(i => (IFolderInfo)i)
                        .ToList();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var folderGroups = folders.GroupBy(f => f.ParentId).OrderBy(g => g.Key != null).ThenBy(g => g.Key).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                FolderTreeBuilder.HandleTree(Folders, folderGroups, (name, id) => new FolderTreeNode(name, id));
            });
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to refresh folder data", e);
        }
    }

    private void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Confirm Selection", Command = new RelayCommand(ConfirmSelection) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Refresh Folders", Command = new RelayCommand(ReloadFolderTree) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, windowService);
    }
}
