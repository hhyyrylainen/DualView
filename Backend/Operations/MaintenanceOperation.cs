using DualView.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Operations;

public class MaintenanceOperation : BaseOperationWithItemCount
{
    public enum MaintenanceTask
    {
        CheckImageFiles,
        DeleteThumbnails,
        PurgeIncorrectlyDeleted,
        FixOrphanedResources
    }

    private readonly MaintenanceTask taskType;
    private readonly Func<MaintenanceOperation, Task<int>> countFunc;
    private readonly Func<MaintenanceOperation, Task<bool>> processFunc;

    public MaintenanceOperation(long id, MaintenanceTask taskType, ILogger logger,
        IServiceScopeFactory scopeFactory,
        Func<MaintenanceOperation, Task<int>> countFunc,
        Func<MaintenanceOperation, Task<bool>> processFunc) : base(id, logger, scopeFactory)
    {
        this.taskType = taskType;
        this.countFunc = countFunc;
        this.processFunc = processFunc;
    }

    public override string Name => taskType.ToString();

    public override OperationStatusUpdate GetStatusUpdate()
    {
        return new OperationStatusUpdate(Id, $"Maintenance: {Name}")
        {
            CompletionFraction = Total > 0 ? (float)Processed / Total : 0,
            Completed = Completed,
            Paused = RunnerPaused,
            Error = HasError
        };
    }

    protected override Task<int> CountTotalItems() => countFunc(this);

    protected override Task<bool> ProcessNextItem() => processFunc(this);
}
