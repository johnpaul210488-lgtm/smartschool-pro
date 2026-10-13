using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Controllers;

/// <summary>
/// Base for all controllers: every response goes out wrapped in the uniform
/// envelope. HTTP status codes are still meaningful (200/201/400/404/409/403),
/// the envelope just makes the body shape constant.
/// </summary>
[ApiController]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    // ---- envelope helpers ----
    protected static ObjectResult OkEnvelope<T>(T data, string message = "OK")
        => new OkObjectResult(ApiResponse<T>.Ok(data, message));

    protected static ObjectResult CreatedEnvelope<T>(T data, string message)
        => new ObjectResult(ApiResponse<T>.Ok(data, message)) { StatusCode = StatusCodes.Status201Created };

    protected static ActionResult OkMessage(string message)
        => new OkObjectResult(ApiResponse.Ok(message));

    protected static ActionResult NotFoundEnvelope(string message)
        => new NotFoundObjectResult(ApiResponse.Fail(message));

    protected static ActionResult BadRequestEnvelope(string message)
        => new BadRequestObjectResult(ApiResponse.Fail(message));

    protected static ActionResult ConflictEnvelope(string message)
        => new ConflictObjectResult(ApiResponse.Fail(message));

    // ---- caller helpers ----
    protected int CallerUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    protected string? CallerRole => User.FindFirstValue(ClaimTypes.Role);
    protected bool IsParent => CallerRole == AppRoles.Parent;
    protected bool IsDriver => CallerRole == AppRoles.Driver;
    protected bool IsStaff => CallerRole is AppRoles.SuperAdmin or AppRoles.Admin;
}
