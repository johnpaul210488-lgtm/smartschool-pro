using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;

namespace SmartSchool.API.Controllers;

/// <summary>FCM device registration. The Flutter apps call register after login + on token refresh.</summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public DevicesController(IUnitOfWork uow) => _uow = uow;

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<object>>> Register(RegisterDeviceRequest request, CancellationToken ct)
    {
        var token = request.Token.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > 500)
            return BadRequestEnvelope("Invalid device token.");

        var platform = request.Platform?.Trim().ToLowerInvariant() is "ios" ? "ios" : "android";

        var existing = await _uow.DeviceTokens.Query(false)
            .FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is null)
        {
            await _uow.DeviceTokens.AddAsync(new DeviceToken
            {
                UserId = CallerUserId, Token = token, Platform = platform
            }, ct);
        }
        else
        {
            // Same physical device, different login (e.g. phone handed over) → re-point at the new user.
            existing.UserId = CallerUserId;
            existing.Platform = platform;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            _uow.DeviceTokens.Update(existing);
        }

        await _uow.SaveChangesAsync(ct);
        return OkEnvelope(new { registered = true }, "Device registered for push notifications.");
    }

    [HttpPost("unregister")]
    public async Task<ActionResult<ApiResponse<object>>> Unregister(RegisterDeviceRequest request, CancellationToken ct)
    {
        var rows = await _uow.DeviceTokens.Query(false)
            .Where(d => d.Token == request.Token && d.UserId == CallerUserId)
            .ToListAsync(ct);
        foreach (var row in rows) _uow.DeviceTokens.Remove(row);
        await _uow.SaveChangesAsync(ct);
        return OkEnvelope(new { removed = rows.Count }, "Device unregistered.");
    }
}
