using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly IJwtTokenService _jwt;

    public AuthController(IUnitOfWork uow, IJwtTokenService jwt)
    {
        _uow = uow;
        _jwt = jwt;
    }

    /// <summary>Exchange username+password for a JWT (WPF, driver and parent apps).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await _uow.Users.Query().FirstOrDefaultAsync(u => u.Username == username, ct);

        // Uniform failure - no account enumeration.
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Unauthorized(ApiResponse.Fail("Invalid username or password."));

        if (!user.IsActive)
            return Unauthorized(ApiResponse.Fail("This account is deactivated. Contact the school."));

        var token = _jwt.CreateToken(user, out var expiresAtUtc);

        var response = new LoginResponse(
            token,
            expiresAtUtc,
            PkTime.NowPk.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture),
            new UserInfoDto(user.Id, user.Username, user.FullName, user.Role.ToString()));

        return OkEnvelope(response, "Login successful.");
    }

    /// <summary>Admin-only account creation. SuperAdmin may create any role; Admin only Teacher/Driver/Parent.</summary>
    [HttpPost("register")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> Register(RegisterUserRequest request, CancellationToken ct)
    {
        if (!IsStaff || (CallerRole != AppRoles.SuperAdmin && request.Role is UserRole.SuperAdmin or UserRole.Admin))
            return Forbid();

        var username = request.Username.Trim();
        if (await _uow.Users.AnyAsync(u => u.Username == username, ct))
            return ConflictEnvelope($"Username '{username}' is already taken.");

        var user = new User
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Role = request.Role,
            PasswordHash = PasswordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await _uow.Users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedEnvelope(
            new UserInfoDto(user.Id, user.Username, user.FullName, user.Role.ToString()),
            $"User '{user.Username}' created successfully.");
    }

    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _uow.Users.FindAsync(new object[] { CallerUserId }, ct);
        if (user is null)
            return Unauthorized(ApiResponse.Fail("Account not found."));

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequestEnvelope("Current password is incorrect.");

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Password changed successfully.");
    }
}
