namespace Backend.Services;

public interface IMaintenanceService
{
    public Task Stop(TimeSpan maxWait);

    public Task<long> StartImageExistCheck();
    public Task<long> StartDeleteThumbnails();
    public Task<long> StartPurgeIncorrectlyDeleted();
    public Task<long> StartFixOrphanedResources();
}
