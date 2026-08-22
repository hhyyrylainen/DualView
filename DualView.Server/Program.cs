using DualView.Server.ClientStubs;
using DualView.Server.Components;
using DualView.Server.Middleware;
using DualView.Server.Services;
using DualView.Shared.Models.Enums;
using DualView.Shared.Services;
using Backend.Database;
using Backend.Plugins;
using Backend.Services;
using ImageMagick;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NLog;

// Toggle this to see or hide EF Core SQL command logs
bool suppressDbCommandLogs = true;

// Set up an initial logger for the bootstrap phase
var bootstrapLogger = new NLogLoggingService("Bootstrap");

var dataFolderService = new DataFolderService("DualView", bootstrapLogger);
dataFolderService.EnsureDataFoldersExist();

// Configure NLog
string logDirectory = dataFolderService.GetLogsFolderPath();
NLogConfigurator.Configure(logDirectory, AppComponent.Server);

// Update the logger after NLog is configured
var logger = new NLogLoggingService("Main");
logger.Info("Application server starting...");

var serverConfig = new ServerConfigurationService(logger, dataFolderService);

// Get the database file path
string dbFilePath = serverConfig.DatabaseFilePath ?? dataFolderService.GetDatabaseFilePath();
logger.Info($"Using database at: {dbFilePath}");

// Create a connection string with WAL mode enabled
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbFilePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared,
    DefaultTimeout = 5000,
    // Ensures PRAGMA foreign_keys=ON for every connection created from this connection string
    ForeignKeys = true,
}.ToString();

// Create and configure the connection with WAL mode (using a temporary connection)
await using (var pragmaConnection = new SqliteConnection(connectionString))
{
    await pragmaConnection.OpenAsync();

    await using var command = pragmaConnection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode=WAL;";
    command.ExecuteNonQuery();

    // Additional optimizations for concurrency
    command.CommandText = "PRAGMA synchronous=NORMAL;";
    command.ExecuteNonQuery();

    // Timeout set above which should hopefully be the same
    /*// Strongly recommended to reduce transient SQLITE_BUSY:
    command.CommandText = "PRAGMA busy_timeout=5000;";
    await command.ExecuteNonQueryAsync();*/
}

//
// Start of usual ASP.NET application
//

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure Microsoft logging with NLog
builder.Services.AddLogging(logBuilder =>
{
    logBuilder.ClearProviders();
    logBuilder.AddProvider(new NLogLoggerProvider());
    logBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);

    if (suppressDbCommandLogs)
    {
        // Specifically suppress the noisy "Executed DbCommand" messages
        logBuilder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",
            Microsoft.Extensions.Logging.LogLevel.Warning);
    }
});

// Register our custom logging service
builder.Services.AddSingleton<ILoggingService>(_ =>
    new NLogLoggingService("DualView.Server"));

// Register DbContext using the connection string so each context gets its own connection
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString, sqliteOptions => { sqliteOptions.CommandTimeout(60); });
    options.UseAsyncSeeding(async (context, _, cancellation) =>
    {
        // Ensure foreign key constraints are enabled
        {
            await using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys;";
            await context.Database.OpenConnectionAsync();
            var enabled = (long)(await cmd.ExecuteScalarAsync())!;
            await context.Database.CloseConnectionAsync();

            if (enabled != 1)
            {
                throw new Exception("Foreign key constraints are disabled");
            }
        }

        await AppDbContext.SeedData(context, cancellation);
    });
});

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

builder.Services.AddControllers();

builder.Services.AddHttpClient();
builder.Services.AddScoped<ILegacyDatabaseImporter, LegacyDatabaseImporter>();

builder.Services.AddSignalR();

// Register data folder service
builder.Services.AddSingleton<IDataFolderService>(dataFolderService);
builder.Services.AddSingleton<IServerConfigurationService>(serverConfig);

// Register other services
builder.Services.AddSingleton<IAppEvents, AppEvents>();
builder.Services.AddSingleton<IBackgroundJobs, BackgroundJobs>();
builder.Services.AddScoped<IDatabaseService, DatabaseService>();
builder.Services.AddScoped<ITagParser, TagParser>();
builder.Services.AddSingleton<ICachedIPResolver, CachedIPResolver>();
builder.Services.AddScoped<IEntityUpdateNotifier, EntityUpdateNotifier>();
builder.Services.AddScoped<IRealTimeDataHub, RealTimeUpdateNotifier>();
builder.Services.AddSingleton<IMaintenanceService, MaintenanceService>();
builder.Services.AddSingleton<ICollectionSimilarityService, CollectionSimilarityService>();
builder.Services.AddSingleton<IImportSectionSimilarityService, ImportSectionSimilarityService>();
builder.Services.AddSingleton<ISystemCheck, SystemCheck>();
builder.Services.AddScoped<IMediaImportHandler, MediaImportHandler>();
builder.Services.AddScoped<IMediaProcessingService, MediaProcessingService>();
builder.Services.AddSingleton<IOperationsStorage, OperationsStorage>();
builder.Services.AddSingleton<ITemporaryFolderService, TemporaryFolderService>();
builder.Services.AddSingleton<IRemoteScanService, RemoteScanService>();
builder.Services.AddSingleton<IRemoteDownloadService, RemoteDownloadService>();
builder.Services.AddScoped<BrowserPluginWebSocketHandler>();
builder.Services.AddSingleton<IPluginRegistry, PluginRegistry>();

// Prerendering compatibility
builder.Services.AddScoped<IClientDatabaseService, DatabaseService>();
builder.Services.AddScoped<ISignalRService, StubSignalRService>();

// TODO: backend Http API to self (or maybe just a dummy that throws if something is used?)

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseWebSockets();

app.UseMiddleware<LocalhostOrBearerTokenMiddleware>();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DualView.Client._Imports).Assembly);

app.MapControllers();

app.Map("/api/{apiVersion}/browser-plugin", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("A websocket connection is required");
        return;
    }

    var capturedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var header in context.Request.Headers)
    {
        if (!header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            capturedHeaders[header.Key] = header.Value.ToString();
    }

    var impersonationHeaders = new BrowserImpersonationHeaders(capturedHeaders);
    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<BrowserPluginWebSocketHandler>();
    await handler.HandleAsync(socket, context.Request.RouteValues["apiVersion"]?.ToString() ?? string.Empty,
        impersonationHeaders, context.RequestAborted);
});

app.MapHub<DataHub>("/hubs/runner");
app.MapHub<RealTimeDataHub>("/hubs/realtime");

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

var plugins = app.Services.GetRequiredService<IPluginRegistry>();

Task? operationsStop = null;

// Register shutdown actions before running
lifetime.ApplicationStopping.Register(() =>
{
    logger.Info("Performing application server shutdown actions...");
    var services = app.Services;

    try
    {
        var backgroundJobs = services.GetService<IBackgroundJobs>();
        var remoteDownloadService = services.GetService<IRemoteDownloadService>();
        var operations = app.Services.GetRequiredService<IOperationsStorage>();

        operationsStop = Task.Run(() => operations.OnAppShutdown());

        plugins.StopAllPlugins().Wait(TimeSpan.FromMinutes(1));

        backgroundJobs?.Stop(true, TimeSpan.FromMinutes(1));
        remoteDownloadService?.Stop(true, TimeSpan.FromMinutes(1));
    }
    catch (Exception e)
    {
        logger.Error(e, "Failed to stop background jobs");
    }
});

// Set ImageMagick limits
ResourceLimits.LimitMemory(new Percentage(40));

// Limit to about 4 gigs at most
ulong reasonableMemoryLimit = 1024L * 1024 * 1024 * 4;

if (ResourceLimits.Memory >= reasonableMemoryLimit)
{
    ResourceLimits.Memory = reasonableMemoryLimit;
}

// Start core services
{
    var serviceScope = app.Services.CreateScope();

    var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();

    // Initialize the database
    await databaseService.InitializeDatabaseAsync();

    if (!string.IsNullOrWhiteSpace(serverConfig.LegacyDatabaseFilePath))
    {
        logger.LogInformation("Starting legacy database import from: {Path}\nThis might take a while!",
            serverConfig.DatabaseFilePath);
        var importer = serviceScope.ServiceProvider.GetRequiredService<ILegacyDatabaseImporter>();
        await importer.ImportAsync(serverConfig.LegacyDatabaseFilePath,
            serverConfig.LegacyCollectionRootPath ??
            Path.GetDirectoryName(Path.GetFullPath(serverConfig.LegacyDatabaseFilePath!))!);
        logger.LogInformation("Legacy import succeeded! Normal app startup will now happen.");
    }
}

var backgroundJobs = app.Services.GetRequiredService<IBackgroundJobs>();
var remoteDownloadService = app.Services.GetRequiredService<IRemoteDownloadService>();
// Make sure some (optional services) are started so that info is available (almost) immediately
var maintenanceJobs = app.Services.GetRequiredService<IMaintenanceService>();

backgroundJobs.Start();
remoteDownloadService.Start();
plugins.InitializeAllPlugins(app.Services).Wait(TimeSpan.FromMinutes(1));

//
// Main start of the application
//
if (!string.IsNullOrEmpty(serverConfig.ListenUrl))
{
    logger.Info($"Overriding listen URL to: {serverConfig.ListenUrl}");
    app.Run(serverConfig.ListenUrl);
}
else
{
    app.Run();
}

// Stop specific services that need some more care on shutdown
var maintenanceStop = maintenanceJobs.Stop(TimeSpan.FromSeconds(60));
backgroundJobs.Stop(true, TimeSpan.FromSeconds(30));
remoteDownloadService.Stop(true, TimeSpan.FromSeconds(30));
maintenanceStop.Wait();
operationsStop?.Wait();

// Make sure services are disposed
await app.DisposeAsync();

logger.Info("Application server shutdown successfully completed");

// Flush and shutdown NLog
logger.Info("Shutting down NLog...");
LogManager.Shutdown();
