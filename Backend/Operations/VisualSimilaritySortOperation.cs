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
    private readonly List<SimilarityImage> images = new();
    private readonly List<(int First, int Second)> comparisons = new();

    private int groupSize = 1;

    private double[,] similarityScores = new double[0, 0];

    private int comparisonIndex;

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
            Message = comparisonIndex == 0
                ? "Loading collection images..."
                : $"Compared {Processed} of {Total} image pairs",
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
        var storage = await MediaImportHandler.GetBaseMediaFolder(databaseService, dataFolderService);

        // TODO: should we load all the images here? This seems pretty long for a first step...
        foreach (var media in collectionItems.Where(media => !media.IsDeleted && media.MediaType.IsImage()))
        {
            var path = Path.Join(storage, media.PathRelativeToStorage());
            var image = new MagickImage(path);
            image.AutoOrient();

            // TODO: pick the most optimal size
            image.Resize(new MagickGeometry(96, 96) { IgnoreAspectRatio = true });
            images.Add(new SimilarityImage(media.Id, image));
        }

        similarityScores = new double[images.Count, images.Count];
        groupSize = Math.Max(1, collection.ImageGroupSize);
        if (groupSize <= 1)
        {
            for (var first = 0; first < images.Count; ++first)
            {
                for (var second = first + 1; second < images.Count; ++second)
                    comparisons.Add((first, second));
            }
        }
        else
        {
            for (var first = 0; first < images.Count; first += groupSize)
            {
                var groupEnd = Math.Min(first + groupSize, images.Count);
                for (var groupFirst = first; groupFirst < groupEnd; ++groupFirst)
                {
                    for (var groupSecond = groupFirst + 1; groupSecond < groupEnd; ++groupSecond)
                        comparisons.Add((groupFirst, groupSecond));
                }
            }
        }

        return comparisons.Count;
    }

    protected override async Task<bool> ProcessNextItem()
    {
        if (comparisonIndex < comparisons.Count)
        {
            var (first, second) = comparisons[comparisonIndex++];
            var score = images[first].Image.Compare(images[second].Image, ErrorMetric.RootMeanSquared);
            similarityScores[first, second] = score;
            similarityScores[second, first] = score;
            if (comparisonIndex < comparisons.Count)
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
            : comparisons.Count == 0
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

        var remaining = new HashSet<int>(Enumerable.Range(0, images.Count));
        var result = new List<long>(images.Count);
        while (remaining.Count > 0)
        {
            var first = remaining.Min();
            remaining.Remove(first);
            result.Add(images[first].MediaId);

            if (remaining.Count == 0)
                break;

            var mostSimilar = remaining.MinBy(index => similarityScores[first, index]);
            remaining.Remove(mostSimilar);
            result.Add(images[mostSimilar].MediaId);
        }

        return result;
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
