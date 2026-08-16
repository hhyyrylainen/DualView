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
public class TagController : Controller
{
    private readonly IDatabaseService databaseService;
    private readonly ITagParser tagParser;

    public TagController(IDatabaseService databaseService, ITagParser tagParser)
    {
        this.databaseService = databaseService;
        this.tagParser = tagParser;
    }

    [HttpGet]
    public async Task<ActionResult<List<TagDTO>>> GetAll()
    {
        return (await databaseService.GetTagsAsync()).ConvertToDTO<Tag, TagDTO>();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TagDTO?>> GetById([Required] long id)
    {
        return (await databaseService.GetTagAsync(id))?.GetDTO();
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<TagDTO>>> Search([Required] [FromQuery] string search)
    {
        return (await databaseService.SearchTagsWildcardAsync(search)).ConvertToDTO<Tag, TagDTO>();
    }

    [HttpGet("byName")]
    public async Task<ActionResult<TagDTO?>> GetByName([Required] [FromQuery] string name)
    {
        return (await databaseService.GetTagByNameAsync(name))?.GetDTO();
    }

    [HttpGet("parse")]
    public async Task<ActionResult<AppliedTagDTO?>> Parse([Required] [FromQuery] string tag)
    {
        var parsed = await tagParser.ParseTag(tag);
        return parsed == null ? Json(null) : await CreateDTO(parsed);
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<List<string>>> Suggestions([Required] [FromQuery] string search,
        [FromQuery] int maxCount = 100)
    {
        return await tagParser.GetSuggestions(search, maxCount);
    }

    [HttpPost]
    public async Task<ActionResult<long>> Create([FromBody] CreateTagRequest request)
    {
        var id = await databaseService.CreateTagAsync(request.Name, request.Category);
        return Ok(id);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update([Required] long id, [FromBody] UpdateTagRequest request)
    {
        await databaseService.UpdateTagAsync(id, request.Name, request.Description, request.Category,
            request.ExampleMediaId);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete([Required] long id)
    {
        await databaseService.DeleteTagAsync(id);
        return Ok();
    }

    [HttpGet("{id:long}/alias")]
    public async Task<ActionResult<List<string>>> GetAliases([Required] long id)
    {
        return await databaseService.GetTagAliasesAsync(id);
    }

    [HttpGet("{id:long}/imply")]
    public async Task<ActionResult<List<TagDTO>>> GetImplies([Required] long id)
    {
        return await databaseService.GetTagImpliesAsync(id);
    }

    [HttpPost("{id:long}/alias")]
    public async Task<IActionResult> CreateAlias([Required] long id, [FromBody] CreateTagAliasRequest request)
    {
        await databaseService.CreateTagAliasAsync(id, request.Alias);
        return Ok();
    }

    [HttpDelete("{id:long}/alias")]
    public async Task<IActionResult> DeleteAlias([Required] long id, [Required] [FromQuery] string alias)
    {
        await databaseService.DeleteTagAliasAsync(id, alias);
        return Ok();
    }

    [HttpPost("{id:long}/imply")]
    public async Task<IActionResult> AddImplication([Required] long id, [FromBody] AddImplicationRequest request)
    {
        await databaseService.AddTagImplicationAsync(id, request.ImpliedTagId);
        return Ok();
    }

    [HttpDelete("{id:long}/imply/{impliedTagId:long}")]
    public async Task<IActionResult> RemoveImplication([Required] long id, [Required] long impliedTagId)
    {
        await databaseService.RemoveTagImplicationAsync(id, impliedTagId);
        return Ok();
    }

    private async Task<AppliedTagDTO> CreateDTO(AppliedTag appliedTag)
    {
        var result = appliedTag.GetDTO();

        // TODO: check if this is necessary (i.e. TagParser doesn't resolve this)
        result.Tag = (await databaseService.GetTagAsync(appliedTag.TagId))?.GetDTO();
        if (appliedTag.CombinedWith != null)
            result.CombinedWith = await CreateDTO(appliedTag.CombinedWith);
        return result;
    }
}
