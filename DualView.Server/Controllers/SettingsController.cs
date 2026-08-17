using DualView.Shared.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class SettingsController : Controller
{
    private readonly ILogger<SettingsController> logger;
    private readonly IDatabaseService databaseService;
    private readonly IAppEvents appEvents;

    public SettingsController(ILogger<SettingsController> logger, IDatabaseService databaseService,
        IAppEvents appEvents)
    {
        this.logger = logger;
        this.databaseService = databaseService;
        this.appEvents = appEvents;
    }

    public async Task<ActionResult<DualViewSettings>> GetSettings()
    {
        var settings = await databaseService.GetAppSettingsAsync();
        return settings;
    }

    [HttpPut]
    public async Task<ActionResult> UpdateSettings([FromBody] DualViewSettings settings)
    {
        var oldSettings = await databaseService.GetAppSettingsAsync();

        oldSettings.UpdateFromClient(settings);

        logger.LogInformation("Updating settings through the API from: {RemoteAddress}",
            Request.HttpContext.Connection.RemoteIpAddress);

        await databaseService.SaveAppSettingsAsync(oldSettings);
        appEvents.NotifySettingsChanged();
        return Ok();
    }

    [HttpPost("regenerateBrowserPluginAccessKey")]
    public async Task<ActionResult<string>> RegenerateBrowserPluginAccessKey()
    {
        var settings = await databaseService.GetAppSettingsAsync();
        settings.BrowserPluginAccessKey = Guid.NewGuid().ToString();

        logger.LogInformation("Regenerated the browser plugin access key through the API from: {RemoteAddress}",
            Request.HttpContext.Connection.RemoteIpAddress);

        await databaseService.SaveAppSettingsAsync(settings);
        appEvents.NotifySettingsChanged();
        return settings.BrowserPluginAccessKey;
    }
}
