using Backend.Models;
using Backend.Services;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Operations;

public sealed class ImportSectionVisualSimilaritySortOperation : BaseOperationWithItemCount
{
    private readonly long sectionId;
    private readonly List<MediaFile> mediaItems = new();
    private readonly List<SimilarityImage> images = new();

    private double[,] similarityScores = new double[0, 0];

    private string? storageLocation;
    private int loadedImageCount;
    private int similarityImageIndex;
    private bool orderSaved;

    public ImportSectionVisualSimilaritySortOperation(long id, long sectionId, ILogger logger,
        IServiceScopeFactory scopeFactory) : base(id, logger, scopeFactory)
    {
        this.sectionId = sectionId;
    }

    public override string Name => "Sort import section by visual similarity";

    public override OperationStatusUpdate GetStatusUpdate()
    {
        return new OperationStatusUpdate(Id, Name)
        {
            CompletionFraction = Total > 0 ? (float)Processed / Total : 0,
            Completed = Completed,
            Paused = RunnerPaused,
            Error = HasError,
            Message = Processed == 0
                ? "Loading import section information..."
                : loadedImageCount < mediaItems.Count
                    ? $"Loaded {loadedImageCount} of {mediaItems.Count} images"
                    : similarityImageIndex < images.Count
                        ? $"Calculating similarities for image {similarityImageIndex + 1} of {images.Count}"
                        : orderSaved ? "Import section reordered" : "Creating sorted order...",
        };
    }

    protected override async Task<int> CountTotalItems()
    {
        var databaseService = CreatedScope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var section = await databaseService.GetUploadSectionAsync(sectionId) ??
                      throw new ArgumentException("Import section not found");
        mediaItems.AddRange(section.Items
            .OrderBy(item => item.Index)
            .Select(item => item.MediaFile)
            .Where(media => !media.IsDeleted && media.MediaType.IsImage()));

        var dataFolderService = CreatedScope.ServiceProvider.GetRequiredService<IDataFolderService>();
        storageLocation = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);
        similarityScores = new double[mediaItems.Count, mediaItems.Count];
        Processed = 1;

        return 1 + mediaItems.Count * 2;
    }

    protected override async Task<bool> ProcessNextItem()
    {
        if (loadedImageCount < mediaItems.Count)
        {
            var media = mediaItems[loadedImageCount++];
            var path = Path.Join(storageLocation, media.PathRelativeToStorage());
            var image = new MagickImage(path);
            image.AutoOrient();
            image.Resize(new MagickGeometry(128, 128) { IgnoreAspectRatio = true });
            images.Add(new SimilarityImage(media.Id, image));
            return true;
        }

        if (similarityImageIndex < images.Count)
        {
            for (var second = similarityImageIndex + 1; second < images.Count; ++second)
            {
                var score = images[similarityImageIndex].Image.Compare(images[second].Image,
                    ErrorMetric.RootMeanSquared);
                similarityScores[similarityImageIndex, second] = score;
                similarityScores[second, similarityImageIndex] = score;
            }

            ++similarityImageIndex;
            return true;
        }

        var databaseService = CreatedScope.ServiceProvider.GetRequiredService<IDatabaseService>();
        await databaseService.ReorderUploadSectionAsync(sectionId, BuildSortedImageIds());
        orderSaved = true;
        DisposeImages();
        return false;
    }

    public override void NotifyShutdown()
    {
        base.NotifyShutdown();
        DisposeImages();
    }

    public override async Task Run()
    {
        try
        {
            await base.Run();
        }
        finally
        {
            DisposeImages();
        }
    }

    private List<long> BuildSortedImageIds()
    {
        var remaining = new HashSet<int>(Enumerable.Range(0, images.Count));
        var sortedIds = new List<long>(images.Count);

        while (remaining.Count >= 2)
        {
            var bestFirst = -1;
            var bestSecond = -1;
            var bestScore = double.PositiveInfinity;
            foreach (var first in remaining)
            {
                foreach (var second in remaining)
                {
                    if (first >= second)
                        continue;

                    var score = similarityScores[first, second];
                    if (score < bestScore || (score.Equals(bestScore) && (first < bestFirst || bestFirst < 0)))
                    {
                        bestFirst = first;
                        bestSecond = second;
                        bestScore = score;
                    }
                }
            }

            sortedIds.Add(images[bestFirst].MediaId);
            sortedIds.Add(images[bestSecond].MediaId);
            remaining.Remove(bestFirst);
            remaining.Remove(bestSecond);
        }

        if (remaining.Count == 1)
            sortedIds.Add(images[remaining.Single()].MediaId);

        return sortedIds;
    }

    private void DisposeImages()
    {
        foreach (var image in images)
            image.Image.Dispose();
        images.Clear();
    }

    private sealed record SimilarityImage(long MediaId, MagickImage Image);
}
