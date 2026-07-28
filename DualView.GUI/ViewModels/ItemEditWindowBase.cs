using System;
using System.Threading.Tasks;
using DualView.GUI.Services;
using DualView.Shared.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

/// <summary>
///   Base class for items edits. Due to some Avalonia weirdness,
///   this cannot be generic so we have to use <c>object</c>.
/// </summary>
public abstract class ItemEditWindowBase : ViewModelBase, IDisposable
{
    protected readonly ILogger? Logger;
    protected readonly IWindowService? WindowService;

    private bool saving;

    public EventHandler? OnWantsToClose;

    // Design time constructor
    protected ItemEditWindowBase()
    {
        Hamburger = new HamburgerMenuViewModel();
    }

    protected ItemEditWindowBase(ILogger logger, IWindowService windowService,
        IBackendStatusService backendStatusService)
    {
        Logger = logger;
        WindowService = windowService;

        Hamburger = new HamburgerMenuViewModel(backendStatusService);
    }

    public long EditedItemId { get; protected set; }

    public bool UnsavedChanges
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? SaveInformationText
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool CloseAfterSaving
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    ///   The primary edited item (once data is loaded from the backend)
    /// </summary>
    public object? Item
    {
        get;
        set
        {
            if (value == field)
                return;

            SetProperty(ref field, value);
            OnItemChanged();
            UnsavedChanges = false;
            SaveInformationText = null;

            if (Item != null)
                ShowLoadingScreen = false;
        }
    }

    public bool ShowLoadingScreen
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public HamburgerMenuViewModel Hamburger { get; }

    protected abstract bool CanRename { get; }

    public virtual void EditItem(long itemId)
    {
        Logger?.LogInformation("Beginning edit of item {Id}", itemId);

        Item = null;
        ShowLoadingScreen = true;
        UnsavedChanges = false;
        EditedItemId = itemId;
        _ = LoadItemData();
    }

    public void ReloadItem()
    {
        _ = LoadItemData();
    }

    public void Save()
    {
        var item = Item;
        if (!UnsavedChanges || item == null)
            return;

        if (saving)
        {
            SaveInformationText = "Already saving!";
            return;
        }

        saving = true;
        Logger?.LogInformation("Beginning save of item {Id}", EditedItemId);
        _ = SaveItemData(item);
    }

    public void SaveAndClose()
    {
        CloseAfterSaving = true;

        if (!UnsavedChanges)
        {
            Logger?.LogInformation("No changes to save, closing window immediately");
            OnWantsToClose?.Invoke(this, EventArgs.Empty);
        }

        Save();
    }

    public void StartRename()
    {
        var item = Item;
        if (WindowService == null || item == null)
            return;

        if (!CanRename)
        {
            WindowService?.ShowErrorWindow("Cannot rename this item", new InvalidOperationException());
            return;
        }

        var editable = new EditableTextWithCallback(GetItemName(item));
        editable.SaveCallback = async (_, _) =>
        {
            SetItemName(item, editable.CurrentText);
            await SaveItemAsync(item);
        };

        WindowService.ShowEditWindow(editable);
    }

    public void MarkDirty()
    {
        UnsavedChanges = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void InitializeMenu()
    {
        MainWindowViewModel.AddDefaultMenuItems(Hamburger);

        AddDerivedMenuItems();

        if (CanRename)
        {
            Hamburger.MenuItems.Add(new HamburgerMenuItem
                { Title = "Rename", Command = new RelayCommand(StartRename) });
        }

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Save", Command = new RelayCommand(Save) });

        Hamburger.MenuItems.Add(new HamburgerMenuItem
            { Title = "Discard Changes", Command = new RelayCommand(ReloadItem) });

        MainWindowViewModel.AddTrailingMenuItems(Hamburger, WindowService);
    }

    protected abstract Task<object?> LoadItemAsync(long itemId);
    protected abstract Task SaveItemAsync(object item);

    protected abstract void AddDerivedMenuItems();

    protected virtual void OnItemChanged()
    {
    }

    /// <summary>
    ///   Checks if the item is valid for saving
    /// </summary>
    /// <param name="item">Item to check</param>
    /// <returns>
    ///   True if valid, false if saving should be prevented.
    ///   Anything that prevents saving should update <see cref="SaveInformationText"/>
    /// </returns>
    protected virtual Task<bool> PreSaveCheck(object item)
    {
        return Task.FromResult(true);
    }

    protected void ReloadItemIfRequired(long noticeItemId)
    {
        // Assume if this happens while saving that it was us
        if (saving)
        {
            Logger?.LogInformation("Skipping update notice while saving");
            return;
        }

        if (EditedItemId == noticeItemId)
        {
            Logger?.LogInformation("We got a notice about update to: {Id}", noticeItemId);

            // If we have unsaved changes, do not reload
            if (UnsavedChanges)
            {
                SaveInformationText =
                    "This item has been edited elsewhere. Please be careful with saving overwriting other changes!";
            }
            else
            {
                ReloadItem();
            }
        }
    }

    protected abstract string GetItemName(object item);
    protected abstract void SetItemName(object item, string newName);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Hamburger.Dispose();
        }
    }

    private async Task LoadItemData()
    {
        var item = EditedItemId;
        try
        {
            var data = await LoadItemAsync(item) ??
                       throw new Exception($"Item with ID {item} not found");

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Item = data;
                }
                catch (Exception e)
                {
                    Logger?.LogError(e, "Failed to show item data");
                    WindowService?.ShowErrorWindow("Error showing item data", e);
                }

                Logger?.LogInformation("Loaded item {Id}", item);
            });
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Failed to load item data");
            WindowService?.ShowErrorWindow("Error loading item data", e);
        }
    }

    private async Task SaveItemData(object item)
    {
        try
        {
            if (!await PreSaveCheck(item))
            {
                Logger?.LogInformation("PreSaveCheck failed, not saving");
                UnsavedChanges = true;
                return;
            }

            await SaveItemAsync(item);
            UnsavedChanges = false;

            Dispatcher.UIThread.Post(() => SaveInformationText = null);

            if (CloseAfterSaving)
            {
                // Wait a bit for final callbacks to run which would otherwise potentially cause disposed errors
                await Task.Delay(200);

                Dispatcher.UIThread.Post(() => OnWantsToClose?.Invoke(this, EventArgs.Empty));
            }
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Failed to save item data");
            WindowService?.ShowErrorWindow("Error saving item data", e);
            UnsavedChanges = true;
        }
        finally
        {
            saving = false;
        }
    }
}
