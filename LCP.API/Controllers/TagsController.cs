using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TagsController : ControllerBase
{
    private readonly ITagService _tagService;

    public TagsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    [HttpGet]
    public async Task<ActionResult<List<string>>> GetAll()
    {
        return await _tagService.GetAllAsync();
    }

    [HttpGet("info")]
    public async Task<ActionResult<List<TagInfo>>> GetInfo()
    {
        return await _tagService.GetInfoAsync();
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return BadRequest();
        await _tagService.AddAsync(tag);
        return NoContent();
    }

    [HttpDelete("{tag}")]
    public async Task<IActionResult> Remove(string tag)
    {
        var removed = await _tagService.RemoveAsync(tag);
        if (!removed) return NotFound();
        return NoContent();
    }
}
