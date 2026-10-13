using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.DTOs;

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed record UserInfoDto(int Id, string Username, string FullName, string Role);

public sealed record LoginResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string ServerTimePakistan,
    UserInfoDto User);

/// <summary>Admins create accounts for staff (and parents) with this.</summary>
public sealed class RegisterUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Parent;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
