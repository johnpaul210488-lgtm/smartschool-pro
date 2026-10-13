using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>Login account for every human that touches the system.</summary>
public sealed class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2-SHA256: {iterations}.{saltBase64}.{hashBase64}</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Optional one-to-one profiles depending on role.
    public Driver? DriverProfile { get; set; }
    public Teacher? TeacherProfile { get; set; }
}
