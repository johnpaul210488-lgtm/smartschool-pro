using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Shared.Common;

namespace SmartSchool.API.Controllers;

/// <summary>Public server info. The Admin app compares this with its own version to spot an outdated server.</summary>
[ApiController]
[Route("api/system")]
[AllowAnonymous]
public sealed class SystemController : BaseApiController
{
    [HttpGet("version")]
    public ActionResult<ApiResponse<object>> Version()
    {
        var raw = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return OkEnvelope<object>(new { version = raw.Split('+')[0] });
    }
}
