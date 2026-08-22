using System.Diagnostics;
using Backend.Models;
using DualView.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class MaintenanceService : IMaintenanceService
{
    private static readonly TimeSpan OldThumbnailTime = TimeSpan.FromDays(90);
    private static readonly TimeSpan OldProcessedFileTime = TimeSpan.FromDays(60);
    private static readonly TimeSpan DeletedImageTime = TimeSpan.FromDays(60);
    private static readonly TimeSpan DeletedCollectionTime = TimeSpan.FromHours(48);
    private static readonly TimeSpan TemporaryMediaTime = TimeSpan.FromHours(72);

    private readonly ILogger<MaintenanceService> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IOperationsStorage operationsStorage;

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly CancellationTokenSource waitCancellationSource = new();

    private readonly Thread maintenanceThread;

    private readonly List<MaintenanceJob> allJobs = new();

    private bool run = true;

    public MaintenanceService(ILogger<MaintenanceService> logger, IServiceScopeFactory scopeFactory,
        IOperationsStorage operationsStorage)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.operationsStorage = operationsStorage;

        allJobs.Add(new DeleteOldThumbnails());
        allJobs.Add(new DeleteOldProcessed());
        allJobs.Add(new PurgeDeletedMedia());
        allJobs.Add(new PurgeDeletedCollections());
        allJobs.Add(new PurgeTemporaryMedia());
        allJobs.Add(new DeleteOrphanedAppliedTags());

        maintenanceThread = new Thread(Run);
        maintenanceThread.Start();
    }

    public async Task Stop(TimeSpan maxWait)
    {
        run = false;
        await waitCancellationSource.CancelAsync();
        cancellationTokenSource.CancelAfter(maxWait / 2);

        var task = Task.Run(() =>
        {
            if (!maintenanceThread.Join(maxWait))
            {
                logger.LogWarning("Failed to wait for maintenance thread to quit");
            }
            else
            {
                logger.LogInformation("Maintenance thread stopped");
            }
        });

        await task;
    }

    public async Task<long> StartImageExistCheck()
    {
        var id = operationsStorage.GetNextOperationId();
        List<long>? allIds = null;
        var errors = new List<string>();

        var op = new Operations.MaintenanceOperation(id,
            Operations.MaintenanceOperation.MaintenanceTask.CheckImageFiles,
            logger, scopeFactory,
            async (m) =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                allIds = await db.GetAllMediaFileIdsAsync();
                return allIds.Count;
            },
            async (m) =>
            {
                if (allIds == null || m.ProcessedCount >= allIds.Count)
                    return false;

                var mediaId = allIds[m.ProcessedCount];

                // It's extremely inefficient to do this for every single media file, but it's better than nothing for now...
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                var dataFolderService = scope.ServiceProvider.GetRequiredService<IDataFolderService>();

                var media = await db.GetMediaByIdAsync(mediaId);
                if (media == null)
                    return m.ProcessedCount + 1 < allIds.Count;

                var storage = await MediaImportHandler.GetBaseMediaFolder(db, dataFolderService);
                var path = Path.Join(storage, media.PathRelativeToStorage());

                if (!File.Exists(path))
                {
                    var error = $"File missing for media {media.Id} ({media.OriginalFileName}): {path}";
                    logger.LogError(error);
                    errors.Add(error);
                    m.SetError();
                }
                else
                {
                    try
                    {
                        using var stream = File.OpenRead(path);
                        var sha = await MediaHash.CalculateMediaHashAsync(stream);

                        if (sha != media.Hash)
                        {
                            var error =
                                $"Hash mismatch for media {media.Id} ({media.OriginalFileName}). Expected: {media.Hash}, Actual: {sha}";
                            logger.LogError(error);
                            errors.Add(error);
                            m.SetError();
                        }
                    }
                    catch (Exception e)
                    {
                        var error =
                            $"Failed to check hash for media {media.Id} ({media.OriginalFileName}): {e.Message}";
                        logger.LogError(e, error);
                        errors.Add(error);
                        m.SetError();
                    }
                }

                if (errors.Count > 0)
                {
                    m.Message = string.Join("\n", errors.TakeLast(5));
                    if (errors.Count > 5)
                        m.Message = $"... (total {errors.Count} errors)\n" + m.Message;
                }

                return m.ProcessedCount + 1 < allIds.Count;
            });
        operationsStorage.RegisterOperation(op);
        _ = Task.Run(() => op.Run());
        return id;
    }

    public async Task<long> StartDeleteThumbnails()
    {
        var id = operationsStorage.GetNextOperationId();
        var op = new Operations.MaintenanceOperation(id,
            Operations.MaintenanceOperation.MaintenanceTask.DeleteThumbnails,
            logger, scopeFactory,
            async (m) => 1,
            async (m) =>
            {
                // Re-use logic from DeleteOldThumbnails but for all
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                var storage = await MediaImportHandler.GetBaseMediaFolder(db,
                    scope.ServiceProvider.GetRequiredService<IDataFolderService>());
                var thumbnails = Path.Join(storage, "thumbnails");
                if (Directory.Exists(thumbnails))
                {
                    Directory.Delete(thumbnails, true);
                    Directory.CreateDirectory(thumbnails);
                }

                return false;
            });
        operationsStorage.RegisterOperation(op);
        _ = Task.Run(() => op.Run());
        return id;
    }

    public async Task<long> StartPurgeIncorrectlyDeleted()
    {
        // TODO: reimplement this operation at some point
        var id = operationsStorage.GetNextOperationId();
        var op = new Operations.MaintenanceOperation(id,
            Operations.MaintenanceOperation.MaintenanceTask.PurgeIncorrectlyDeleted,
            logger, scopeFactory,
            async (m) => 0,
            async (m) => false);
        operationsStorage.RegisterOperation(op);
        _ = Task.Run(() => op.Run());
        return id;
    }

    public async Task<long> StartFixOrphanedResources()
    {
        var id = operationsStorage.GetNextOperationId();
        var op = new Operations.MaintenanceOperation(id,
            Operations.MaintenanceOperation.MaintenanceTask.FixOrphanedResources,
            logger, scopeFactory,
            async (m) => 3,
            async (m) =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

                if (m.ProcessedCount == 0)
                {
                    var orphanedMedia = await db.GetOrphanedMediaFilesAsync();
                    if (orphanedMedia.Count > 0)
                    {
                        var uncategorizedCollection = await db.GetCollectionAsync(Collection.UncategorizedCollectionId);
                        if (uncategorizedCollection != null)
                        {
                            var sequenceNumber =
                                await db.GetNextCollectionSequenceNumberAsync(Collection.UncategorizedCollectionId);
                            await db.AddMediaToCollection(orphanedMedia, Collection.UncategorizedCollectionId,
                                sequenceNumber);

                            m.Message =
                                $"Added {orphanedMedia.Count} orphaned media files to Uncategorized collection.";
                        }
                        else
                        {
                            logger.LogWarning("Uncategorized collection not found during fix");
                            m.Message = "Error: Uncategorized collection not found";
                            m.SetError();
                        }
                    }
                    else
                    {
                        m.Message = "No orphaned media files found.";
                    }

                    return true;
                }

                if (m.ProcessedCount == 1)
                {
                    var orphanedCollections = await db.GetOrphanedCollectionsAsync();
                    if (orphanedCollections.Count > 0)
                    {
                        var rootFolder = await db.GetMediaFolderAsync(MediaFolder.RootFolderId);
                        if (rootFolder != null)
                        {
                            foreach (var collection in orphanedCollections)
                            {
                                collection.Folders.Add(rootFolder);
                                await db.SaveCollectionAsync(collection);
                            }

                            m.Message = (m.Message ?? "") +
                                        $"\nAdded {orphanedCollections.Count} orphaned collections to Root folder.";
                        }
                        else
                        {
                            logger.LogWarning("Root folder not found during fix");
                            m.Message = (m.Message ?? "") + "\nError: Root folder not found";
                            m.SetError();
                        }
                    }
                    else
                    {
                        m.Message = (m.Message ?? "") + "\nNo orphaned collections found.";
                    }

                    return true;
                }

                if (m.ProcessedCount == 2)
                {
                    var orphanedFolders = await db.GetOrphanedMediaFoldersAsync();
                    if (orphanedFolders.Count > 0)
                    {
                        var rootFolder = await db.GetMediaFolderAsync(MediaFolder.RootFolderId);
                        if (rootFolder != null)
                        {
                            foreach (var folder in orphanedFolders)
                            {
                                folder.Parents.Add(rootFolder);
                                await db.SaveMediaFolderAsync(folder);
                            }

                            m.Message = (m.Message ?? "") +
                                        $"\nAdded {orphanedFolders.Count} orphaned folders to Root folder.";
                        }
                        else
                        {
                            logger.LogWarning("Root folder not found during fix");
                            m.Message = (m.Message ?? "") + "\nError: Root folder not found";
                            m.SetError();
                        }
                    }
                    else
                    {
                        m.Message = (m.Message ?? "") + "\nNo orphaned folders found.";
                    }

                    return false;
                }

                return false;
            });
        operationsStorage.RegisterOperation(op);
        _ = Task.Run(() => op.Run());
        return id;
    }

    private static TimeSpan GetJobRandomizationTime()
    {
        return TimeSpan.FromSeconds(new Random().NextSingle() * 60);
    }

    private void Run()
    {
        var cancellationToken = cancellationTokenSource.Token;

        var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waitCancellationSource.Token);
        var waitToken = waitCancellation.Token;

        // We do not want to cancel waiting for the necessary job records
        // ReSharper disable once MethodSupportsCancellation
        CreateNeededJobs(cancellationToken).Wait();

        // Wait a bit before the first maintenance jobs are allowed to trigger
        try
        {
            Task.Delay(TimeSpan.FromSeconds(30), waitToken).Wait(cancellationToken);
        }
        catch (Exception)
        {
            logger.LogInformation("Initial wait before maintenance thread started was skipped");
        }

        bool ranSomething = false;

        while (run)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                Task.Delay(TimeSpan.FromMilliseconds(500), waitToken).Wait(cancellationToken);
            }
            catch (Exception)
            {
                break;
            }

            if (!ranSomething)
            {
                // Wait for more jobs to become available
                try
                {
                    Task.Delay(TimeSpan.FromMinutes(1), waitToken).Wait(cancellationToken);
                }
                catch (Exception)
                {
                    break;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            ranSomething = false;

            {
                using var scope = scopeFactory.CreateScope();
                var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

                // Find the oldest job we want to run
                var toRun = databaseService.GetOldestMaintenanceJobToRun(DateTime.UtcNow).WaitAsync(cancellationToken)
                    .Result;

                if (toRun == null)
                {
                    continue;
                }

                // We have a job to run, run it
                logger.LogInformation("Running maintenance job {JobName}", toRun.Name);
                var job = allJobs.FirstOrDefault(j => j.Name == toRun.Name);

                if (job == null)
                {
                    logger.LogWarning(
                        "Maintenance job {JobName} not found! Deleting its record (assuming it is an old job)",
                        toRun.Name);

                    databaseService.DeleteMaintenanceRecord(toRun).Wait(cancellationToken);
                    continue;
                }

                ranSomething = true;

                try
                {
                    var stopWatch = Stopwatch.StartNew();
                    stopWatch.Start();

                    var task = job.Run(toRun, databaseService, scope, cancellationToken);
                    task.Wait(cancellationToken);
                    if (task.Exception != null)
                        throw task.Exception;

                    if (toRun.Failed)
                    {
                        logger.LogError("Maintenance job {JobName} failed: {StatusMessage}", toRun.Name,
                            toRun.StatusMessage);
                    }
                    else
                    {
                        logger.LogInformation("Maintenance job {JobName} succeeded in {Elapsed}", toRun.Name,
                            stopWatch.Elapsed);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to run maintenance job {JobName}", toRun.Name);
                    toRun.Failed = true;
                    toRun.StatusMessage = $"Failed to run: {e.Message}";
                    ranSomething = false;
                    continue;
                }

                toRun.NextRunAfter = DateTime.UtcNow + job.Interval + GetJobRandomizationTime();

                try
                {
                    var task = databaseService.SaveMaintenanceRecord(toRun);

                    // We want to always save even if a cancellation is requested
                    // ReSharper disable once MethodSupportsCancellation
                    task.Wait();
                    if (task.Exception != null)
                        throw task.Exception;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to save maintenance job record");
                }
            }
        }
    }

    private async Task CreateNeededJobs(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        foreach (var job in allJobs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var jobRecord = await databaseService.GetMaintenanceRecord(job.Name);

            if (jobRecord == null)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                logger.LogInformation("Creating job record that is missing for {JobName}", job.Name);
                await databaseService.CreateMaintenanceRecord(new MaintenanceJobRecord(job.Name)
                {
                    LastPerformed = DateTime.UtcNow,
                    NextRunAfter = DateTime.UtcNow + job.Interval + GetJobRandomizationTime(),
                });
            }
        }
    }

    private abstract class MaintenanceJob
    {
        public abstract string Name { get; }
        public abstract TimeSpan Interval { get; }
        protected virtual bool IsPurgeJob => false;

        public virtual async Task Run(MaintenanceJobRecord jobRecord, IDatabaseService databaseService,
            IServiceScope scope, CancellationToken cancellationToken)
        {
            jobRecord.StatusMessage = string.Empty;

            if (IsPurgeJob && (await databaseService.GetAppSettingsAsync()).HoldPurge)
            {
                jobRecord.StatusMessage = "Skipped because purge hold is enabled";
                jobRecord.LastPerformed = DateTime.UtcNow;
                jobRecord.Failed = false;
                return;
            }

            if (await RunInternal(jobRecord, databaseService, scope, cancellationToken))
            {
                jobRecord.LastPerformed = DateTime.UtcNow;
                jobRecord.Failed = false;

                if (cancellationToken.IsCancellationRequested)
                {
                    if (string.IsNullOrEmpty(jobRecord.StatusMessage))
                        jobRecord.StatusMessage = "Interrupted";
                }
            }
            else
            {
                jobRecord.Failed = true;

                if (string.IsNullOrEmpty(jobRecord.StatusMessage))
                    jobRecord.StatusMessage = "Failed to run";
            }
        }

        protected abstract Task<bool> RunInternal(MaintenanceJobRecord jobRecord, IDatabaseService databaseService,
            IServiceScope scope,
            CancellationToken cancellationToken);
    }

    private class DeleteOldThumbnails : MaintenanceJob
    {
        public override string Name => "DeleteOldThumbnails";

        public override TimeSpan Interval => TimeSpan.FromDays(2);

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            int deleted = 0;

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DeleteOldThumbnails>>();

            var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService,
                scope.ServiceProvider.GetRequiredService<IDataFolderService>());

            var thumbnails = Path.Join(storage, "thumbnails");

            var directoryInfo = new DirectoryInfo(thumbnails);
            if (directoryInfo.Exists)
            {
                var cutoffTime = DateTime.UtcNow - OldThumbnailTime;

                int itemsChecked = 0;

                foreach (var fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (fileInfo.CreationTimeUtc < cutoffTime)
                    {
                        try
                        {
                            fileInfo.Delete();
                            ++deleted;
                        }
                        catch (Exception e)
                        {
                            // Log but continue processing other files
                            logger.LogWarning(e, "Failed to delete old thumbnail file {File}", fileInfo.FullName);
                        }
                    }

                    if (++itemsChecked % 1000 == 0)
                    {
                        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                jobRecord.StatusMessage = "No thumbnails folder found";
                return true;
            }

            jobRecord.StatusMessage = $"Deleted {deleted} old thumbnails";
            return true;
        }
    }

    private class DeleteOldProcessed : MaintenanceJob
    {
        public override string Name => "DeleteOldProcessed";

        public override TimeSpan Interval => TimeSpan.FromDays(2);

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            int deleted = 0;

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DeleteOldProcessed>>();

            var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService,
                scope.ServiceProvider.GetRequiredService<IDataFolderService>());

            var processed = Path.Join(storage, "processed");

            var directoryInfo = new DirectoryInfo(processed);
            if (directoryInfo.Exists)
            {
                var cutoffTime = DateTime.UtcNow - OldProcessedFileTime;

                int itemsChecked = 0;

                foreach (var fileInfo in directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (fileInfo.CreationTimeUtc < cutoffTime)
                    {
                        try
                        {
                            fileInfo.Delete();
                            ++deleted;
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "Failed to delete old processed file {File}", fileInfo.FullName);
                        }
                    }

                    if (++itemsChecked % 1000 == 0)
                    {
                        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                jobRecord.StatusMessage = "No processed files folder found";
                return true;
            }

            jobRecord.StatusMessage = $"Deleted {deleted} old processed files";
            return true;
        }
    }

    private class PurgeDeletedMedia : MaintenanceJob
    {
        public override string Name => "PurgeDeletedMedia";
        public override TimeSpan Interval => TimeSpan.FromDays(1);
        protected override bool IsPurgeJob => true;

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            // Purge MediaFiles that are deleted for a while
            var filesToPurge = await databaseService.GetEligibleMediaFilesForPurgeAsync(DeletedImageTime);
            int filesPurged = 0;

            foreach (var mediaFile in filesToPurge)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await databaseService.PurgeMediaAsync(mediaFile.Id);
                filesPurged++;
            }

            jobRecord.StatusMessage = $"Purged {filesPurged} media files";
            return true;
        }
    }

    private class PurgeDeletedCollections : MaintenanceJob
    {
        public override string Name => "PurgeDeletedCollections";
        public override TimeSpan Interval => TimeSpan.FromHours(6);
        protected override bool IsPurgeJob => true;

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            var collectionsToPurge = await databaseService.GetEligibleCollectionsForPurgeAsync(DeletedCollectionTime);
            var collectionsPurged = 0;
            foreach (var collection in collectionsToPurge)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await databaseService.PurgeCollectionAsync(collection.Id);
                ++collectionsPurged;
            }

            jobRecord.StatusMessage = $"Purged {collectionsPurged} collections";
            return true;
        }
    }

    private class PurgeTemporaryMedia : MaintenanceJob
    {
        public override string Name => "PurgeTemporaryMedia";
        public override TimeSpan Interval => TimeSpan.FromDays(1);
        protected override bool IsPurgeJob => true;

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            var temporaryMedia = await databaseService.GetEligibleTemporaryMediaFilesForPurgeAsync(TemporaryMediaTime);
            var mediaPurged = 0;
            foreach (var media in temporaryMedia)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await databaseService.DeleteMediaAsync(media.Id);
                await databaseService.PurgeMediaAsync(media.Id);
                ++mediaPurged;
            }

            jobRecord.StatusMessage = $"Purged {mediaPurged} temporary media files";
            return true;
        }
    }

    private class DeleteOrphanedAppliedTags : MaintenanceJob
    {
        public override string Name => "DeleteOrphanedAppliedTags";
        public override TimeSpan Interval => TimeSpan.FromDays(14);

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            await databaseService.DeleteOrphanedAppliedTagsAsync();
            jobRecord.StatusMessage = "Deleted orphaned applied tags";
            return true;
        }
    }
}
