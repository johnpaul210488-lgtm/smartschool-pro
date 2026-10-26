namespace SmartSchool.Core.DTOs;

public sealed record DriverListItem(
    int Id,
    string FullName,
    string Phone,
    string LicenseNo,
    int UserId,
    string Username,
    bool IsActive,
    int? AssignedBusId,
    string? AssignedBusNumber);

public sealed class DriverCreateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;

    /// <summary>The driver's mobile-app login (Role=Driver).</summary>
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Driver ka mobile-app login badalna: username (optional change) aur naya password (khali ho to nahi badalta).</summary>
public sealed class DriverLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string? NewPassword { get; set; }
}

public sealed class DriverUpdateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
