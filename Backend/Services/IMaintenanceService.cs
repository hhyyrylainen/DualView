namespace Backend.Services;

public interface IMaintenanceService
{
    public Task Stop(TimeSpan maxWait);
}
