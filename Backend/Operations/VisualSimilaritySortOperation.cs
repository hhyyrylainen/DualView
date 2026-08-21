using Backend.Models;
using Backend.Services;
using DualView.Shared.Models;
using DualView.Shared.Models.Enums;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Operations;

public sealed class VisualSimilaritySortOperation : BaseOperationWithItemCount
{
    private readonly long collectionId;
    private readonly List<MediaFile> orderedCollectionItems = new();
    private readonly List<MediaFile> imageItems = new();
    private readonly List<SimilarityImage> images = new();

    private int groupSize = 1;

    private double[,] similarityScores = new double[0, 0];

    private string? storageLocation;
    private int loadedImageCount;
    private int similarityImageIndex;

    public IReadOnlyList<long>? SortedOrder { get; private set; }

    public VisualSimilaritySortOperation(long id, long collectionId, ILogger logger,
        IServiceScopeFactory scopeFactory) : base(id, logger, scopeFactory)
    {
        this.collectionId = collectionId;
    }

    public override string Name => "Sort by visual similarity";

    public override OperationStatusUpdate GetStatusUpdate()
    {
        return new OperationStatusUpdate(Id, Name)
        {
            CompletionFraction = Total > 0 ? (float)Processed / Total : 0,
            Completed = Completed,
            Paused = RunnerPaused,
            Error = HasError,
            Message = Processed == 0
                ? "Loading collection information..."
                : loadedImageCount < imageItems.Count
                    ? $"Loaded {loadedImageCount} of {imageItems.Count} images"
                    : similarityImageIndex < images.Count
                        ? $"Calculating similarities for image {similarityImageIndex + 1} of {images.Count}"
                        : "Creating sorted order...",
        };
    }

    protected override async Task<int> CountTotalItems()
    {
        var databaseService = CreatedScope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var collection = await databaseService.GetCollectionAsync(collectionId) ??
                         throw new ArgumentException("Collection not found");
        var collectionItems = await databaseService.GetCollectionContents(collectionId);
        orderedCollectionItems.AddRange(collectionItems);

        var dataFolderService = CreatedScope.ServiceProvider.GetRequiredService<IDataFolderService>();
        storageLocation = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);

        imageItems.AddRange(collectionItems.Where(media => !media.IsDeleted && media.MediaType.IsImage()));

        groupSize = Math.Max(1, collection.ImageGroupSize);
        similarityScores = new double[imageItems.Count, imageItems.Count];

        // Count the database-loading phase as the first completed step.
        Processed = 1;

        return 1 + imageItems.Count * 2;
    }

    protected override async Task<bool> ProcessNextItem()
    {
        if (loadedImageCount < imageItems.Count)
        {
            var media = imageItems[loadedImageCount++];
            var path = Path.Join(storageLocation, media.PathRelativeToStorage());
            var image = new MagickImage(path);
            image.AutoOrient();

            // 96 is a bit too low to get consistent results. So 128 is used for now.
            // TODO: pick the most optimal size
            image.Resize(new MagickGeometry(128, 128) { IgnoreAspectRatio = true });
            images.Add(new SimilarityImage(media.Id, image));
            return true;
        }

        if (similarityImageIndex < images.Count)
        {
            var groupEnd = groupSize <= 1
                ? images.Count
                : Math.Min((similarityImageIndex / groupSize + 1) * groupSize, images.Count);
            for (var second = similarityImageIndex + 1; second < groupEnd; ++second)
            {
                var score = images[similarityImageIndex].Image.Compare(images[second].Image,
                    ErrorMetric.RootMeanSquared);
                similarityScores[similarityImageIndex, second] = score;
                similarityScores[second, similarityImageIndex] = score;
            }

            ++similarityImageIndex;
            return true;
        }

        CreateSortedOrder();
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

    private void CreateSortedOrder()
    {
        if (images.Count < 2)
        {
            SortedOrder = orderedCollectionItems.Select(media => media.Id).ToList();
            return;
        }

        var sortedImageIds = images.Count == 0
            ? new List<long>()
            : images.Count == 1
                ? images.Select(image => image.MediaId).ToList()
                : BuildSortedImageIds();
        var sortedImageIndex = 0;
        var sortedOrder = new List<long>(orderedCollectionItems.Count);
        foreach (var media in orderedCollectionItems)
        {
            if (media.MediaType.IsImage() && !media.IsDeleted)
            {
                sortedOrder.Add(sortedImageIds[sortedImageIndex++]);
            }
            else
            {
                sortedOrder.Add(media.Id);
            }
        }

        SortedOrder = sortedOrder;
    }

    private List<long> BuildSortedImageIds()
    {
        if (groupSize > 1)
        {
            return images.Select((image, index) => (image, index))
                .GroupBy(item => item.index / groupSize)
                .OrderBy(group => group.Average(item => GetScoreForGroup(group, item.index)))
                .SelectMany(group => group.Select(item => item.image.MediaId))
                .ToList();
        }

        return BuildGreedyInsertedImageOrder()
            .Select(index => images[index].MediaId)
            .ToList();
    }

    private List<int> BuildGreedyInsertedImageOrder()
    {
        if (images.Count == 0)
            return new List<int>();

        if (images.Count == 1)
            return [0];

        var remaining = new HashSet<int>(Enumerable.Range(0, images.Count));

        var bestFirst = 0;
        var bestSecond = 1;
        var bestScore = similarityScores[bestFirst, bestSecond];

        for (var first = 0; first < images.Count; ++first)
        {
            for (var second = first + 1; second < images.Count; ++second)
            {
                var score = similarityScores[first, second];
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFirst = first;
                    bestSecond = second;
                }
            }
        }

        var ordered = new List<int> { bestFirst, bestSecond };
        remaining.Remove(bestFirst);
        remaining.Remove(bestSecond);

        while (remaining.Count > 0)
        {
            var bestCandidate = -1;
            var bestInsertIndex = -1;
            var bestInsertCost = double.PositiveInfinity;

            foreach (var candidate in remaining)
            {
                for (var insertIndex = 0; insertIndex <= ordered.Count; ++insertIndex)
                {
                    var cost = GetInsertionCost(ordered, insertIndex, candidate);

                    if (cost < bestInsertCost ||
                        (cost.Equals(bestInsertCost) && candidate < bestCandidate))
                    {
                        bestInsertCost = cost;
                        bestCandidate = candidate;
                        bestInsertIndex = insertIndex;
                    }
                }
            }

            ordered.Insert(bestInsertIndex, bestCandidate);
            remaining.Remove(bestCandidate);
        }

        return ordered;
    }

    private double GetInsertionCost(IReadOnlyList<int> ordered, int insertIndex, int candidate)
    {
        if (insertIndex == 0)
            return similarityScores[candidate, ordered[0]];

        if (insertIndex == ordered.Count)
            return similarityScores[ordered[^1], candidate];

        var previous = ordered[insertIndex - 1];
        var next = ordered[insertIndex];

        return similarityScores[previous, candidate]
               + similarityScores[candidate, next]
               - similarityScores[previous, next];
    }

    private double GetScoreForGroup(IEnumerable<(SimilarityImage image, int index)> group, int itemIndex)
    {
        var indices = group.Select(item => item.index).ToList();
        return indices.Where(index => index != itemIndex)
            .Select(index => similarityScores[itemIndex, index])
            .DefaultIfEmpty(0)
            .Average();
    }

    private void DisposeImages()
    {
        foreach (var image in images)
            image.Image.Dispose();
        images.Clear();
    }

    private sealed record SimilarityImage(long MediaId, MagickImage Image);
}
