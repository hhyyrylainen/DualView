using System;
using System.Threading.Tasks;
using DualView.Shared.Models;
using DualView.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.ViewModels;

public class TextEditWindowViewModel : ViewModelBase
{
    private readonly ILogger<TextEditWindowViewModel>? logger;
    private readonly IClientDatabaseService? databaseService;
    private readonly IBackendAPI? backendAPI;

    public event EventHandler? OnWantsToClose;

    // Design time constructor
    public TextEditWindowViewModel()
    {
        Error = "Example error message";
    }

    [ActivatorUtilitiesConstructor]
    public TextEditWindowViewModel(ILogger<TextEditWindowViewModel> logger, IClientDatabaseService databaseService,
        IBackendAPI backendAPI)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.backendAPI = backendAPI;
    }

    /// <summary>
    ///   The main thing to edit. Should be set before showing this window!
    /// </summary>
    public IEditableText ThingToEdit
    {
        get;
        set
        {
            if (field == value)
                return;

            SetProperty(ref field, value);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(TextToEdit));
            CanSave = false;
        }
    } = new DummyEdit();

    public bool CanSave
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Title => $"DualView -  {ThingToEdit.EditTitle}";

    public string TextToEdit
    {
        get => ThingToEdit.CurrentText;
        set
        {
            if (ThingToEdit.CurrentText == value)
                return;

            ThingToEdit.CurrentText = value;

            // Can now save after changes
            CanSave = true;
        }
    }

    public string? Error
    {
        get;
        set => SetProperty(ref field, value);
    }

    public void TrySave()
    {
        logger?.LogInformation("Starting save of text edit");
        CanSave = false;
        _ = PerformSave();
    }

    public void Cancel()
    {
        if (OnWantsToClose == null)
            logger?.LogWarning("No cancel is hooked up for this window");

        OnWantsToClose?.Invoke(this, EventArgs.Empty);
    }

    private async Task PerformSave()
    {
        if (databaseService == null || backendAPI == null)
            return;

        string? error = null;

        try
        {
            await ThingToEdit.SaveChangesAsync(databaseService, backendAPI);
        }
        catch (Exception e)
        {
            error = "Failed to save: " + e.Message;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (error != null)
            {
                Error = error;

                // Allow trying again
                CanSave = true;
            }
            else
            {
                Error = null;

                // Succeeded, so close this
                OnWantsToClose?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private class DummyEdit : IEditableText
    {
        public string CurrentText { get; set; } = "ERROR: uninitialized";
        public string EditTitle => "Dummy Edit";

        public Task SaveChangesAsync(IClientDatabaseService databaseService, IBackendAPI backendAPI)
        {
            throw new NotSupportedException("This is a dummy edit, so no saving is possible");
        }
    }
}
