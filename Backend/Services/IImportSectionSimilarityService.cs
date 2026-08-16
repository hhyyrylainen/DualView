namespace Backend.Services;

public interface IImportSectionSimilarityService
{
    public Task<long> StartSortByVisualSimilarity(long sectionId);
}
