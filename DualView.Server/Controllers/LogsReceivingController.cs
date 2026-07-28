using DualView.Shared.Requests;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/logs")]
public class LogsReceivingController : Controller
{
    private readonly ILogger<LogsReceivingController> logger;

    public LogsReceivingController(ILogger<LogsReceivingController> logger)
    {
        this.logger = logger;
    }

    [HttpPost]
    public IActionResult ReceiveClientLog([FromBody] LogForwardRequest log)
    {
        if (log.Message.Length > 10000 || log.Exception?.Length > 10000 || string.IsNullOrEmpty(log.Message))
            return BadRequest("Invalid log message");

        if (!string.IsNullOrEmpty(log.Exception))
        {
            logger.LogError("[CLIENT-LOG] {Message}\n[CLIENT-EXCEPTION] {Exception}", log.Message, log.Exception);
        }
        else
        {
            // Map string level back to internal log level
            switch (log.Level)
            {
                case "Warning":
                    logger.LogWarning("[CLIENT-LOG] {Message}", log.Message);
                    break;
                case "Error":
                case "Fatal":
                    logger.LogError("[CLIENT-LOG] {Message}", log.Message);
                    break;
                default:
                    logger.LogInformation("[CLIENT-LOG] {Message}", log.Message);
                    break;
            }
        }

        return Ok();
    }
}
