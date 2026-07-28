using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

/// <summary>
///   Allows sending messages to long-running background operations
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class OperationController : Controller
{
    private readonly ILogger<OperationController> logger;
    private readonly IOperationsStorage operationsStorage;

    public OperationController(ILogger<OperationController> logger, IOperationsStorage operationsStorage)
    {
        this.logger = logger;
        this.operationsStorage = operationsStorage;
    }

    [HttpGet("{id:long}")]
    public ActionResult<OperationStatusUpdate> GetOperationStatus([Required] long id)
    {
        var operation = operationsStorage.GetOperation(id);

        if (operation == null)
        {
            return new OperationStatusUpdate(-1, "Operation not found")
            {
                Error = true,
            };
        }

        return operation.GetStatusUpdate();
    }

    [HttpPost("{id:long}/pause")]
    public ActionResult PauseOperation([Required] long id)
    {
        var operation = operationsStorage.GetOperation(id);

        if (operation == null)
            return NotFound();

        if (!operation.CanPause)
            return BadRequest("Operation type doesn't support pausing");

        operation.Pause();
        return Ok();
    }

    [HttpPost("{id:long}/resume")]
    public ActionResult ResumeOperation([Required] long id)
    {
        var operation = operationsStorage.GetOperation(id);

        if (operation == null)
            return NotFound();

        if (!operation.CanPause)
            return BadRequest("Operation type doesn't support pausing");

        operation.Resume();
        return Ok();
    }

    [HttpPost("{id:long}/cancel")]
    [HttpDelete("{id:long}")]
    public ActionResult CancelOperation([Required] long id)
    {
        var operation = operationsStorage.GetOperation(id);

        if (operation == null)
            return NotFound();

        if (!operation.CanCancel)
            return BadRequest("Operation type doesn't support canceling");

        logger.LogInformation("Canceling operation {Id}", id);
        operation.Cancel();
        return Ok();
    }
}
