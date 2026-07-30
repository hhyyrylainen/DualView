using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Services;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CollectionController : Controller
{
    private readonly IDatabaseService databaseService;

    public CollectionController(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CollectionDTO?>> GetById([Required] long id)
    {
        var collection = await databaseService.GetCollectionAsync(id);
        return collection?.GetDTO();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<List<CollectionDTO>>> GetDeleted([FromQuery] int limit = 100)
    {
        return await ((IClientDatabaseService)databaseService).GetDeletedCollectionsAsync(limit);
    }

    [HttpPost("{id:long}/restore")]
    public async Task<IActionResult> Restore([Required] long id)
    {
        await databaseService.RestoreCollectionAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/purge")]
    public async Task<IActionResult> Purge([Required] long id)
    {
        await databaseService.PurgeCollectionAsync(id);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete([Required] long id)
    {
        await databaseService.DeleteCollectionAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/addToFolder/{folderId:long}")]
    public async Task<IActionResult> AddToFolder([Required] long id, [Required] long folderId)
    {
        await databaseService.AddCollectionToFolder(id, folderId);
        return Ok();
    }
}
