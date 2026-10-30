using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;

namespace SmartSchool.API.Controllers;

/// <summary>FCM device registration. The Flutter apps call register after login + on token refresh.</summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IUnitOfWork uow,
        IConfiguration configuration,
        ILogger<DevicesController> logger)
    {
        _uow = uow;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Safe staff diagnostic: reports whether the server can send FCM and how
    /// many parent accounts/devices are ready, without exposing paths or tokens.
    /// </summary>
    [HttpGet("push-status")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<PushServiceStatus>>> PushStatus(CancellationToken ct)
    {
        var enabled = _configuration.GetValue("Firebase:Enabled", false);
        var configuredPath = _configuration["Firebase:CredentialsJsonPath"];
        var credentialsPresent = !string.IsNullOrWhiteSpace(configuredPath)
                                 && System.IO.File.Exists(configuredPath);
        var credentialsValid = credentialsPresent && IsServiceAccountFile(configuredPath!);
        var linkedParents = await _uow.ParentStudents.Query()
            .Where(link => link.ParentUser.IsActive && link.ParentUser.Role == UserRole.Parent)
            .Select(link => link.ParentUserId)
            .Distinct()
            .CountAsync(ct);
        var registeredDevices = await _uow.DeviceTokens.Query()
            .CountAsync(device => device.User.IsActive && device.User.Role == UserRole.Parent, ct);

        var detail = !enabled
            ? "Firebase is disabled on the school server. Run Queen School - Firebase Setup, then restart the API."
            : !credentialsPresent
                ? "Firebase is enabled but the protected Admin SDK service-account file is missing/unreadable."
                : !credentialsValid
                    ? "The configured Firebase file is not a valid Admin SDK service-account JSON."
                    : registeredDevices == 0
                        ? "Firebase is ready, but no parent phone has registered an FCM token yet."
                        : "Firebase server credentials and parent device registration are ready.";

        return OkEnvelope(new PushServiceStatus(
            enabled,
            credentialsPresent,
            credentialsValid,
            enabled && credentialsValid,
            linkedParents,
            registeredDevices,
            detail));
    }

    private static bool IsServiceAccountFile(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = document.RootElement;
            return root.TryGetProperty("type", out var type)
                   && type.GetString() == "service_account"
                   && root.TryGetProperty("project_id", out var project)
                   && project.GetString() == "queen-school"
                   && root.TryGetProperty("client_email", out var email)
                   && !string.IsNullOrWhiteSpace(email.GetString())
                   && root.TryGetProperty("private_key", out var key)
                   && key.GetString()?.StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) == true;
        }
        catch
        {
            return false;
        }
    }

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
        // Never write the FCM token itself to the logs.
        _logger.LogInformation("FCM device registered for user {UserId} on {Platform}.", CallerUserId, platform);
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

public sealed record PushServiceStatus(
    bool Enabled,
    bool CredentialsPresent,
    bool CredentialsValid,
    bool Ready,
    int LinkedParentAccounts,
    int RegisteredParentDevices,
    string Detail);
