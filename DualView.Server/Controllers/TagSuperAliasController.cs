using DualView.Shared.Models.DTO;
using DualView.Shared.Requests;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace DualView.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TagSuperAliasController : Controller
{
    private readonly IDatabaseService databaseService;

    public TagSuperAliasController(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    [HttpGet]
    public async Task<ActionResult<List<TagSuperAliasDTO>>> GetAll()
    {
        return (await databaseService.GetTagSuperAliasesAsync())
            .Select(item => new TagSuperAliasDTO(item.Alias, item.Expanded)).ToList();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTagSuperAliasRequest request)
    {
        await databaseService.CreateTagSuperAliasAsync(request.Alias, request.Expanded);
        return Ok();
    }

    [HttpPut("{originalAlias}")]
    public async Task<IActionResult> Update(string originalAlias, [FromBody] UpdateTagSuperAliasRequest request)
    {
        await databaseService.UpdateTagSuperAliasAsync(originalAlias, request.Alias, request.Expanded);
        return Ok();
    }
}
