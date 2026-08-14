namespace Backend.Services;

public interface ICollectionSimilarityService
{
    public Task<long> StartSortByVisualSimilarity(long collectionId);

    public List<long> GetVisualSimilarityOrder(long operationId);
}
