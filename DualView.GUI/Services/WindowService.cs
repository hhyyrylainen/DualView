using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using DualView.GUI.Models;
using DualView.GUI.ViewModels;
using DualView.Shared.Models;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualView.GUI.Services;

public sealed class WindowService : IWindowService
{
    private readonly ILogger<WindowService> logger;
    private readonly IServiceProvider services;
    private readonly IWindowRecoveryService windowRecoveryService;

    private readonly ConcurrentDictionary<Type, Window> singletons = new();

    public WindowService(ILogger<WindowService> logger, IServiceProvider services,
        IWindowRecoveryService windowRecoveryService)
    {
        this.logger = logger;
        this.services = services;
        this.windowRecoveryService = windowRecoveryService;
    }

    public TViewModel? ShowSingletonWindow<TViewModel>()
        where TViewModel : class
    {
        var vmType = typeof(TViewModel);

        if (singletons.TryGetValue(vmType, out var existing) && existing is { IsVisible: true })
        {
            existing.Activate();
            existing.Focus();
            return (TViewModel?)existing.DataContext;
        }

        var serviceScope = services.CreateScope();

        // Resolve VM and make it
        var vm = serviceScope.ServiceProvider.GetRequiredService<TViewModel>();

        var window = PerformWindowCreation(vm, serviceScope);
        windowRecoveryService.RegisterSingletonWindow(window, vmType);
        return vm;
    }

    public void ShowWindow<TViewModel>(Action<TViewModel>? onCreated = null)
        where TViewModel : class
    {
        var serviceScope = services.CreateScope();
        var vm = serviceScope.ServiceProvider.GetRequiredService<TViewModel>();
        onCreated?.Invoke(vm);

        var window = PerformWindowCreation(vm, serviceScope);
        if (vm is MediaCollectionWindowViewModel collectionViewModel && collectionViewModel.CollectionId.HasValue)
            windowRecoveryService.RegisterCollectionWindow(window, collectionViewModel.CollectionId.Value);
    }

    /// <summary>
    ///   Shows a window for the given ViewModel.
    /// </summary>
    /// <param name="viewModel">View model to show a window for</param>
    /// <param name="serviceScope">
    ///   If not null, this should be the scope for the model.
    ///   This is disposed of automatically when the window closes.
    /// </param>
    /// <typeparam name="TViewModel">Type of the view model</typeparam>
    public void ShowSingletonWindow<TViewModel>(TViewModel viewModel, IServiceScope? serviceScope = null)
        where TViewModel : class
    {
        var vmType = typeof(TViewModel);

        if (singletons.TryGetValue(vmType, out var existing) && existing is { IsVisible: true })
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var window = PerformWindowCreation(viewModel, serviceScope);
        windowRecoveryService.RegisterSingletonWindow(window, vmType);
    }

    public void ShowErrorWindow(string errorTitle, Exception exception)
    {
        // Make sure this triggers on the main thread (if called on another thread)
        Dispatcher.UIThread.Invoke(async () =>
        {
            logger.LogInformation("Going to show an error window: {Title}", errorTitle);
            logger.LogInformation("Window error: {Exception}", exception);

            // Wait a tiny bit in case another window is opening so that the error is on top
            await Task.Delay(100);

            // If already open, just update the error and focus it
            if (singletons.TryGetValue(typeof(ErrorWindowViewModel), out var existing) &&
                existing is { IsVisible: true })
            {
                if (existing.DataContext is ErrorWindowViewModel errorModel)
                {
                    existing.Activate();
                    existing.Focus();

                    // If the title is the same, add a number to it
                    if (errorModel.Title == errorTitle || errorModel.Title.EndsWith($" ({errorModel.ErrorCount})"))
                    {
                        errorModel.ErrorCount += 1;
                        errorModel.Title = $"{errorTitle} ({errorModel.ErrorCount})";
                    }
                    else
                    {
                        // New error
                        errorModel.Title = errorTitle;
                        errorModel.ErrorCount = 1;
                    }

                    errorModel.ErrorMessage = $"An unhandled error has occurred ({exception.GetType().Name})";
                    errorModel.Details = exception.ToString();

                    return;
                }

                logger.LogError("Failed to reuse existing error window: DataContext is not an ErrorWindowViewModel");
            }

            var serviceScope = services.CreateScope();

            var vm = serviceScope.ServiceProvider.GetRequiredService<ErrorWindowViewModel>();

            vm.Title = errorTitle;
            vm.ErrorMessage = $"An unhandled error has occurred ({exception.GetType().Name})";
            vm.Details = exception.ToString();

            ShowSingletonWindow(vm, serviceScope);
        });
    }

    public void ShowNoticeWindow(string message, string? customTitle)
    {
        // Make sure this triggers on the main thread (if called on another thread)
        Dispatcher.UIThread.Invoke(async () =>
        {
            logger.LogInformation("Going to show notice with message: {Message}", message);

            // Ordering of this is not as serious so trigger soon
            await Task.Delay(10);

            // If already open, just update the error and focus it
            if (singletons.TryGetValue(typeof(InformationWindowViewModel), out var existing) &&
                existing is { IsVisible: true })
            {
                if (existing.DataContext is InformationWindowViewModel infoModel)
                {
                    existing.Activate();
                    existing.Focus();

                    if (!string.IsNullOrWhiteSpace(customTitle))
                    {
                        infoModel.Title = customTitle;
                    }
                    else
                    {
                        infoModel.Title = "Information";
                    }

                    infoModel.Message = message;
                    return;
                }

                logger.LogError(
                    "Failed to reuse existing error window: DataContext is not an InformationWindowViewModel");
            }

            var serviceScope = services.CreateScope();

            var vm = serviceScope.ServiceProvider.GetRequiredService<InformationWindowViewModel>();

            if (!string.IsNullOrWhiteSpace(customTitle))
                vm.Title = customTitle;

            vm.Message = message;

            ShowSingletonWindow(vm, serviceScope);
        });
    }

    public void ShowEditWindow(IEditableText editable)
    {
        var serviceScope = services.CreateScope();
        var editViewModel = new TextEditWindowViewModel(
            serviceScope.ServiceProvider.GetRequiredService<ILogger<TextEditWindowViewModel>>(),
            serviceScope.ServiceProvider.GetRequiredService<IClientDatabaseService>(),
            serviceScope.ServiceProvider.GetRequiredService<IBackendAPI>())
        {
            ThingToEdit = editable,
        };

        PerformInstanceWindowCreation(editViewModel, serviceScope);
    }

    public async Task<bool?> ShowConfirmationWindow(string title, string question, bool allowCancel = false)
    {
        var serviceScope = services.CreateScope();
        var confirmationViewModel =
            new ConfirmationWindowViewModel(serviceScope.ServiceProvider
                .GetRequiredService<ILogger<ConfirmationWindowViewModel>>())
            {
                Title = title,
                Message = question,
                ShowCancel = allowCancel,
            };

        var future = new TaskCompletionSource<bool?>();

        confirmationViewModel.OnOptionSelected += result => future.SetResult(result);

        // If this was called on another thread, we need to show the window on the UI thread
        await Dispatcher.UIThread.InvokeAsync(() => PerformInstanceWindowCreation(confirmationViewModel, serviceScope));

        // Wait for the dialog to be used.
        // TODO: do we need some safety against getting stuck here?
        return await future.Task;
    }

    public void ShowMediaViewer(IVisualMediaSource mediaSource, ICollectionBrowse? collectionBrowse)
    {
        var serviceScope = services.CreateScope();
        var editViewModel = new ImageViewerWindowViewModel(
            serviceScope.ServiceProvider.GetRequiredService<ILogger<ImageViewerWindowViewModel>>(),
            serviceScope.ServiceProvider.GetRequiredService<IWindowService>(),
            serviceScope.ServiceProvider.GetRequiredService<IBackendStatusService>(),
            serviceScope.ServiceProvider.GetRequiredService<IClientDatabaseService>(),
            serviceScope.ServiceProvider.GetRequiredService<ISignalRService>(),
            serviceScope.ServiceProvider.GetRequiredService<IBackendAPI>());

        editViewModel.ShowMedia(mediaSource, null, collectionBrowse);

        var window = PerformInstanceWindowCreation(editViewModel, serviceScope);

        void OnDisplayedMediaChanged(long mediaId)
        {
            windowRecoveryService.UpdateMediaViewer(window, mediaId);
        }

        editViewModel.OnDisplayedMediaChanged += OnDisplayedMediaChanged;
        window.Closed += (_, _) => editViewModel.OnDisplayedMediaChanged -= OnDisplayedMediaChanged;

        if (mediaSource is ServerMediaSource serverSource)
        {
            long? collectionId = collectionBrowse is CollectionBrowse browse ? browse.CollectionId : null;
            windowRecoveryService.RegisterMediaViewer(window, serverSource.ServerId, collectionId);
        }
    }

    public void ShowOperationStatus(long operationId)
    {
        var serviceScope = services.CreateScope();
        var operationViewModel = new OperationStatusWindowViewModel(
            serviceScope.ServiceProvider.GetRequiredService<ILogger<OperationStatusWindowViewModel>>(),
            serviceScope.ServiceProvider.GetRequiredService<ISignalRService>(),
            serviceScope.ServiceProvider.GetRequiredService<IBackendAPI>());

        operationViewModel.OperationId = operationId;

        // Allow calling from a background thread
        Dispatcher.UIThread.Post(() => PerformInstanceWindowCreation(operationViewModel, serviceScope));
    }

    // TODO: change this into a parent / siblings view
    public void ShowGeneratedMediaView(GeneratedMediaSource source, long sourceId,
        Action<IVisualMediaSource>? onMediaSelected)
    {
        var serviceScope = services.CreateScope();
        /*var editViewModel = new GeneratedMediaForWindowViewModel(
            serviceScope.ServiceProvider.GetRequiredService<ILogger<GeneratedMediaForWindowViewModel>>(),
            serviceScope.ServiceProvider.GetRequiredService<IWindowService>(),
            serviceScope.ServiceProvider.GetRequiredService<IClientDatabaseService>(),
            serviceScope.ServiceProvider.GetRequiredService<IBackendStatusService>(),
            serviceScope.ServiceProvider, onMediaSelected != null);

        editViewModel.OnMediaSelected = onMediaSelected;
        editViewModel.Setup(source, sourceId);

        PerformInstanceWindowCreation(editViewModel, serviceScope);*/
    }

    public void ShowMediaEditSetup(long mediaConfigurationId)
    {
        var serviceScope = services.CreateScope();
        var vm = serviceScope.ServiceProvider.GetRequiredService<MediaEditSelectorWindowViewModel>();

        vm.ShowFor(mediaConfigurationId);

        Dispatcher.UIThread.Post(() => PerformInstanceWindowCreation(vm, serviceScope));
    }

    public void ShowMediaEditor(long mediaConfigurationId)
    {
        var serviceScope = services.CreateScope();
        var vm = serviceScope.ServiceProvider.GetRequiredService<MediaEditorWindowViewModel>();

        vm.InitializeFor(mediaConfigurationId);

        Dispatcher.UIThread.Post(() => PerformInstanceWindowCreation(vm, serviceScope));
    }

    public void ShowEditMediaFolders(long mediaConfigurationId)
    {
        var scope = services.CreateScope();
        var vm = ActivatorUtilities.CreateInstance<EditMediaFoldersWindowViewModel>(scope.ServiceProvider);
        vm.Initialize(mediaConfigurationId);
        Dispatcher.UIThread.Post(() => PerformInstanceWindowCreation(vm, scope));
    }

    public void ShowEditMediaFolders(IConfiguredMediaInfo item)
    {
        var scope = services.CreateScope();
        var vm = ActivatorUtilities.CreateInstance<EditMediaFoldersWindowViewModel>(scope.ServiceProvider);
        vm.Initialize(item);
        Dispatcher.UIThread.Post(() => PerformInstanceWindowCreation(vm, scope));
    }

    public void ShowTextInputWindow(string title, string explanation, string? initialValue,
        Func<TextInputWindowViewModel, Task<bool>> onAccept, string? placeholder = null)
    {
        var vm = new TextInputWindowViewModel(onAccept)
        {
            Title = title,
            ExplanationText = explanation,
            Input = initialValue,
            InputPlaceholder = placeholder ?? "Input...",
        };

        PerformInstanceWindowCreation(vm, null);
    }

    private static Window CreateWindowForViewModel(object viewModel)
    {
        // Convention: SettingsWindowViewModel -> SettingsWindow
        var vmType = viewModel.GetType();

        // Handle namespace first
        var name = vmType.FullName!.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);

        // And then the class name
        var viewTypeName = name.Replace("ViewModel", string.Empty);
        var viewType = Type.GetType(viewTypeName, throwOnError: false);

        if (viewType is null || !typeof(Window).IsAssignableFrom(viewType))
        {
            throw new InvalidOperationException(
                $"No Window found for ViewModel {vmType.Name}. Expected {viewTypeName}");
        }

        var window = (Window)Activator.CreateInstance(viewType)!;
        window.DataContext = viewModel;
        return window;
    }

    private Window PerformWindowCreation<TViewModel>(TViewModel viewModel, IServiceScope? serviceScope)
        where TViewModel : class
    {
        var vmType = typeof(TViewModel);

        var view = CreateWindowForViewModel(viewModel);

        // Track lifetime
        view.Closed += (_, _) =>
        {
            windowRecoveryService.UnregisterWindow(view);
            singletons.TryRemove(vmType, out var _);
            (viewModel as IDisposable)?.Dispose();
            serviceScope?.Dispose();
        };

        singletons[vmType] = view;

        /*// Show relative to the main window if available
        var app = Application.Current;
        var owner = (app?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null && view != owner)
            view.Show(owner);
        else*/
        view.Show();

        view.Activate();
        return view;
    }

    private Window PerformInstanceWindowCreation<TViewModel>(TViewModel viewModel, IServiceScope? serviceScope,
        Action? onClose = null)
        where TViewModel : class
    {
        var view = CreateWindowForViewModel(viewModel);

        // Dispose of things once done
        view.Closed += (_, _) =>
        {
            windowRecoveryService.UnregisterWindow(view);
            (viewModel as IDisposable)?.Dispose();
            serviceScope?.Dispose();
            onClose?.Invoke();
        };

        // TODO: could try to add a parameter for a parent window for this

        view.Show();

        view.Activate();
        return view;
    }
}
