namespace Backend.Services;

public interface ICollectionSimilarityService
{
    public Task<long> StartSortByVisualSimilarity(long collectionId);

    public Task<long> StartSortByVisualSimilarity(long collectionId, IReadOnlyList<long> selectedImageIds);

    public List<long> GetVisualSimilarityOrder(long operationId);
}
