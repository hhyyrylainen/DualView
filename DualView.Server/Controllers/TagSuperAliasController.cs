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
    private readonly ITagParser tagParser;

    public TagSuperAliasController(IDatabaseService databaseService, ITagParser tagParser)
    {
        this.databaseService = databaseService;
        this.tagParser = tagParser;
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
        var validationError = await ValidateExpansion(request.Alias, request.Expanded);
        if (validationError != null)
            return validationError;

        await databaseService.CreateTagSuperAliasAsync(request.Alias, request.Expanded);
        return Ok();
    }

    [HttpPut("{originalAlias}")]
    public async Task<IActionResult> Update(string originalAlias, [FromBody] UpdateTagSuperAliasRequest request)
    {
        var validationError = await ValidateExpansion(request.Alias, request.Expanded);
        if (validationError != null)
            return validationError;

        await databaseService.UpdateTagSuperAliasAsync(originalAlias, request.Alias, request.Expanded);
        return Ok();
    }

    private async Task<BadRequestObjectResult?> ValidateExpansion(string alias, string expanded)
    {
        if (alias.Trim().Equals(expanded.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest("A super alias cannot expand to itself");

        if (await tagParser.ParseTag(expanded) == null)
            return BadRequest("The expanded super alias text could not be parsed as a tag");

        return null;
    }
}
