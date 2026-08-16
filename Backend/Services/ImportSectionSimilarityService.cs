using Backend.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public sealed class ImportSectionSimilarityService : IImportSectionSimilarityService
{
    private readonly ILogger<ImportSectionSimilarityService> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IOperationsStorage operationsStorage;

    public ImportSectionSimilarityService(ILogger<ImportSectionSimilarityService> logger,
        IServiceScopeFactory scopeFactory, IOperationsStorage operationsStorage)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.operationsStorage = operationsStorage;
    }

    public Task<long> StartSortByVisualSimilarity(long sectionId)
    {
        var operationId = operationsStorage.GetNextOperationId();
        var operation = new ImportSectionVisualSimilaritySortOperation(operationId, sectionId, logger, scopeFactory);
        operationsStorage.RegisterOperation(operation);
        _ = Task.Run(operation.Run);
        return Task.FromResult(operationId);
    }
}
