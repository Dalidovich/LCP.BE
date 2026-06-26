using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/production-info")]
public class ProductionInfoController : ControllerBase
{
    private readonly IProductionInfoService _productionInfoService;
    private readonly ISettingsRepository _settingsRepository;

    public ProductionInfoController(IProductionInfoService productionInfoService, ISettingsRepository settingsRepository)
    {
        _productionInfoService = productionInfoService;
        _settingsRepository = settingsRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<string>>> GetAll([FromQuery] bool filterByType = false)
    {
        List<VideoType>? typeFilter = null;
        if (filterByType)
        {
            var settings = await _settingsRepository.GetAsync();
            if (settings?.VideoTypeFilter is { Count: > 0 })
                typeFilter = settings.VideoTypeFilter;
        }
        return await _productionInfoService.GetAllAsync(typeFilter);
    }

    [HttpGet("info")]
    public async Task<ActionResult<List<ProductionInfoDto>>> GetInfo([FromQuery] bool filterByType = false)
    {
        List<VideoType>? typeFilter = null;
        if (filterByType)
        {
            var settings = await _settingsRepository.GetAsync();
            if (settings?.VideoTypeFilter is { Count: > 0 })
                typeFilter = settings.VideoTypeFilter;
        }
        return await _productionInfoService.GetInfoAsync(typeFilter);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] string studio)
    {
        if (string.IsNullOrWhiteSpace(studio)) return BadRequest();
        await _productionInfoService.AddAsync(studio);
        return NoContent();
    }

    [HttpDelete("{studio}")]
    public async Task<IActionResult> Remove(string studio)
    {
        var removed = await _productionInfoService.RemoveAsync(studio);
        if (!removed) return NotFound();
        return NoContent();
    }
}
