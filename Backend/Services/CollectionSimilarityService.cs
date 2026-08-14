using Backend.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

public sealed class CollectionSimilarityService : ICollectionSimilarityService
{
    private readonly ILogger<CollectionSimilarityService> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IOperationsStorage operationsStorage;

    public CollectionSimilarityService(ILogger<CollectionSimilarityService> logger,
        IServiceScopeFactory scopeFactory, IOperationsStorage operationsStorage)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.operationsStorage = operationsStorage;
    }

    public Task<long> StartSortByVisualSimilarity(long collectionId)
    {
        var operationId = operationsStorage.GetNextOperationId();
        var operation = new VisualSimilaritySortOperation(operationId, collectionId, logger, scopeFactory);
        operationsStorage.RegisterOperation(operation);
        _ = Task.Run(operation.Run);
        return Task.FromResult(operationId);
    }

    public List<long> GetVisualSimilarityOrder(long operationId)
    {
        var operation = operationsStorage.GetOperation(operationId) as VisualSimilaritySortOperation ??
                        throw new KeyNotFoundException("Visual similarity operation not found");
        if (!operation.Completed)
            throw new InvalidOperationException("Visual similarity operation has not completed");
        if (operation.HasError || operation.SortedOrder == null)
            throw new InvalidOperationException("Visual similarity operation failed");

        var order = operation.SortedOrder.ToList();
        operationsStorage.RemoveOperation(operationId);
        return order;
    }
}
