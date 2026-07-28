using System.Diagnostics;
using Backend.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public class MaintenanceService : IMaintenanceService
{
    private static readonly TimeSpan OldThumbnailTime = TimeSpan.FromDays(90);
    private static readonly TimeSpan OldProcessedFileTime = TimeSpan.FromDays(60);

    private readonly ILogger<MaintenanceService> logger;
    private readonly IServiceScopeFactory scopeFactory;

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly CancellationTokenSource waitCancellationSource = new();

    private readonly Thread maintenanceThread;

    private readonly List<MaintenanceJob> allJobs = new();

    private bool run = true;

    public MaintenanceService(ILogger<MaintenanceService> logger, IServiceScopeFactory scopeFactory)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;

        allJobs.Add(new DeleteOldThumbnails());
        allJobs.Add(new DeleteOldProcessed());
        allJobs.Add(new PurgeDeletedMedia());

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

        public virtual async Task Run(MaintenanceJobRecord jobRecord, IDatabaseService databaseService,
            IServiceScope scope, CancellationToken cancellationToken)
        {
            jobRecord.StatusMessage = string.Empty;

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

        protected override async Task<bool> RunInternal(MaintenanceJobRecord jobRecord,
            IDatabaseService databaseService, IServiceScope scope, CancellationToken cancellationToken)
        {
            var dataFolderService = scope.ServiceProvider.GetRequiredService<IDataFolderService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<PurgeDeletedMedia>>();
            var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);

            // Purge MediaFiles that are deleted and not marked keep
            var filesToPurge = await databaseService.GetEligibleMediaFilesForPurgeAsync();
            int filesPurged = 0;

            foreach (var mediaFile in filesToPurge)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var path = Path.Join(storage, mediaFile.PathRelativeToStorage());
                var croppedPath = Path.Join(storage, mediaFile.CroppedPathRelativeToStorage());

                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to delete physical media file {Path}", path);
                    }
                }

                if (File.Exists(croppedPath))
                {
                    try
                    {
                        File.Delete(croppedPath);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to delete physical cropped media file {Path}", croppedPath);
                    }
                }

                await databaseService.PurgeMediaFileAsync(mediaFile);
                filesPurged++;
            }

            jobRecord.StatusMessage = $"Purged {filesPurged} media files";
            return true;
        }
    }
}
