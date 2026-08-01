using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.ViewModels;

public class MediaEditSelectorWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IWindowService? windowService;
    private readonly IClientDatabaseService? clientDatabaseService;

    private readonly Regex oldNameCleanUp = new(@" ?\(\d+\)$");

    private long mediaId = -1;

    public MediaEditSelectorWindowViewModel()
    {
        NewFolders.Add(new NewFolderViewModel("New Folder"));
        NewFolders.Add(new NewFolderViewModel("A test folder/with some path/")
        {
            Selected = true,
        });
        NewFolders.Add(new NewFolderViewModel("Outputs/Images/Model/2026-02-22/"));

        SiblingItems.Add(new SiblingItemViewModel(new ConfiguredMediaDTO("Some_stuff_j2221", string.Empty), _ => { }));
        SiblingItems.Add(new SiblingItemViewModel(new ConfiguredMediaDTO("Another sibling", string.Empty), _ => { }));
        SiblingItems.Add(new SiblingItemViewModel(new ConfiguredMediaDTO("Third sibling", string.Empty), _ => { }));
        SiblingItems.Add(
            new SiblingItemViewModel(new ConfiguredMediaDTO("Sibling with a pretty long name", string.Empty),
                _ => { }));
    }

    [ActivatorUtilitiesConstructor]
    public MediaEditSelectorWindowViewModel(IWindowService windowService, IClientDatabaseService clientDatabaseService)
    {
        this.windowService = windowService;
        this.clientDatabaseService = clientDatabaseService;
    }

    public bool Loading
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool WantsToClose
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool CloseAfterSelecting
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public string? NewName
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? NewExtraFolderToAdd
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Name => ShownMedia?.Name ?? "No media selected";

    public ConfiguredMediaDTO? ShownMedia
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(Name));
        }
    }

    public ObservableCollection<SiblingItemViewModel> SiblingItems { get; } = new();

    public ObservableCollection<NewFolderViewModel> NewFolders { get; } = new();

    public void ShowFor(long mediaConfigurationId)
    {
        if (mediaConfigurationId == mediaId)
            return;

        mediaId = mediaConfigurationId;
        RefreshData();
    }

    public void EditMain()
    {
        // TODO: redo this selector to be refactored away just entirely, DualView allows cropping the main image
        if (ShownMedia == null)
        {
            windowService?.ShowNoticeWindow("Cannot edit the main media, create a new variant");
            return;
        }

        if (ShownMedia.IsDeleted)
        {
            windowService?.ShowNoticeWindow("Cannot edit a deleted media");
            return;
        }

        OnSelectionMade(ShownMedia);
    }

    public void CreateNewAndEdit()
    {
        if (string.IsNullOrEmpty(NewName))
        {
            windowService?.ShowNoticeWindow("Please enter a name for the new media");
            return;
        }

        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(NewExtraFolderToAdd))
            folders.Add(NewExtraFolderToAdd);

        foreach (var newFolderViewModel in NewFolders)
        {
            if (newFolderViewModel.Selected)
                folders.Add(newFolderViewModel.Name);
        }

        if (folders.Count < 1)
        {
            windowService?.ShowNoticeWindow("Please select at least one folder for the new media");
            return;
        }

        _ = PerformCreation(NewName, folders);
    }

    public void Dispose()
    {
        SiblingItems.Clear();
    }

    private void RefreshData()
    {
        _ = FetchDataAsync();
    }

    private async Task FetchDataAsync()
    {
        if (clientDatabaseService == null)
            return;

        Loading = true;

        try
        {
            var mediaInfo = await clientDatabaseService.GetConfiguredMediaAsync(mediaId);

            if (mediaInfo == null)
                throw new Exception("Media not found");

            // Get folders for new folder creation
            var folders = await clientDatabaseService.GetConfiguredMediaFoldersAsync(mediaId);

            // Get other configurations
            var otherConfigs = await clientDatabaseService.GetConfiguredMediaSiblingsAsync(mediaId);

            // Send to GUI for user selection
            Dispatcher.UIThread.Post(() =>
            {
                ShownMedia = mediaInfo;
                Loading = false;

                SiblingItems.Clear();

                foreach (var other in otherConfigs)
                {
                    SiblingItems.Add(new SiblingItemViewModel(other, OnSelectionMade));
                }

                NewName = null;
                var nameBase = oldNameCleanUp.Replace(mediaInfo.Name, "");

                if (!nameBase.EndsWith('_'))
                    nameBase += ' ';

                for (int i = 2; i < 10000; ++i)
                {
                    var newName = nameBase + $"({i})";

                    if (mediaInfo.Name == newName || otherConfigs.Any(c => c.Name == newName))
                        continue;

                    NewName = newName;
                    break;
                }

                NewFolders.Clear();
                NewFolders.Add(new NewFolderViewModel(folders.PrimaryFolder)
                {
                    Selected = true,
                });

                if (folders.SecondaryFolders != null)
                {
                    foreach (var other in folders.SecondaryFolders)
                    {
                        NewFolders.Add(new NewFolderViewModel(other));
                    }
                }
            });
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to fetch media data", e);
        }
    }

    private void OnSelectionMade(ConfiguredMediaDTO selectedItem)
    {
        windowService?.ShowMediaEditor(selectedItem.Id);

        if (CloseAfterSelecting)
            WantsToClose = true;
    }

    private async Task PerformCreation(string newName, List<string> folders)
    {
        var parentId = ShownMedia?.MediaFileId;

        if (parentId == null || clientDatabaseService == null)
        {
            windowService?.ShowNoticeWindow("Media not found");
            return;
        }

        try
        {
            var newConfig = await clientDatabaseService.CreateConfiguredMediaAsync(parentId.Value, newName, folders);

            OnSelectionMade(newConfig);
        }
        catch (Exception e)
        {
            windowService?.ShowErrorWindow("Failed to create media", e);
        }
    }

    public class SiblingItemViewModel : ViewModelBase
    {
        private readonly ConfiguredMediaDTO item;
        private readonly Action<ConfiguredMediaDTO> selectAction;

        public SiblingItemViewModel(ConfiguredMediaDTO item, Action<ConfiguredMediaDTO> selectAction)
        {
            this.item = item;
            this.selectAction = selectAction;
        }

        public string Name => item.Name;

        public bool Enabled => true;

        public void OnSelect()
        {
            selectAction(item);
        }
    }

    public class NewFolderViewModel : ViewModelBase
    {
        public NewFolderViewModel(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool Selected
        {
            get;
            set => SetProperty(ref field, value);
        }
    }
}
