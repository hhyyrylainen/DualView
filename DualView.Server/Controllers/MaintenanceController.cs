using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MaintenanceController : Controller
{
    private readonly IMaintenanceService maintenanceService;

    public MaintenanceController(IMaintenanceService maintenanceService)
    {
        this.maintenanceService = maintenanceService;
    }

    [HttpPost("checkFiles")]
    public async Task<ActionResult<long>> StartImageExistCheck()
    {
        return await maintenanceService.StartImageExistCheck();
    }

    [HttpPost("deleteThumbnails")]
    public async Task<ActionResult<long>> StartDeleteThumbnails()
    {
        return await maintenanceService.StartDeleteThumbnails();
    }

    [HttpPost("purgeIncorrectlyDeleted")]
    public async Task<ActionResult<long>> StartPurgeIncorrectlyDeleted()
    {
        return await maintenanceService.StartPurgeIncorrectlyDeleted();
    }

    [HttpPost("fixOrphaned")]
    public async Task<ActionResult<long>> StartFixOrphanedResources()
    {
        return await maintenanceService.StartFixOrphanedResources();
    }
}
