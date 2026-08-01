using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;
using DualView.Shared.Requests;
using Backend.Models;
using Backend.Services;
using Backend.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TagModifierController : Controller
{
    private readonly IDatabaseService databaseService;

    public TagModifierController(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    [HttpGet]
    public async Task<ActionResult<List<TagModifierDTO>>> GetAll()
    {
        return (await databaseService.GetTagModifiersAsync()).ConvertToDTO<TagModifier, TagModifierDTO>();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TagModifierDTO?>> GetById([Required] long id)
    {
        return (await databaseService.GetTagModifierAsync(id))?.GetDTO();
    }

    [HttpPost]
    public async Task<ActionResult<long>> Create([FromBody] CreateModifierRequest request)
    {
        var id = await databaseService.CreateTagModifierAsync(request.Name);
        return Ok(id);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update([Required] long id, [FromBody] UpdateModifierRequest request)
    {
        await databaseService.UpdateTagModifierAsync(id, request.Name, request.Description);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete([Required] long id)
    {
        await databaseService.DeleteTagModifierAsync(id);
        return Ok();
    }

    [HttpPost("{id:long}/alias")]
    public async Task<IActionResult> CreateAlias([Required] long id, [FromBody] CreateTagModifierAliasRequest request)
    {
        await databaseService.CreateTagModifierAliasAsync(id, request.Alias);
        return Ok();
    }

    [HttpDelete("{id:long}/alias")]
    public async Task<IActionResult> DeleteAlias([Required] long id, [Required] [FromQuery] string alias)
    {
        await databaseService.DeleteTagModifierAliasAsync(id, alias);
        return Ok();
    }
}
