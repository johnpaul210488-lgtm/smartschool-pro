using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Services.IoT;

namespace SmartSchool.API.Controllers;

/// <summary>IoT portal integration ko remotely diagnose karne ke liye live probe.</summary>
[ApiController]
[Route("api/iot")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class IotController : ControllerBase
{
    private readonly ThingWorxClient _twx;
    public IotController(ThingWorxClient twx) => _twx = twx;

    /// <summary>Portal se sahi mein seekha kya (laast relay/discovery report).</summary>
    [HttpGet("report")]
    public IActionResult Report() => Ok(new { report = _twx.LastDiscoveryReport });

    /// <summary>Fresh discovery abhi chalao (5-10 sec lagenge).</summary>
    [HttpGet("discover")]
    public async Task<IActionResult> Discover(CancellationToken ct)
        => Ok(new { report = await _twx.DiscoverAsync(ct) });
}
