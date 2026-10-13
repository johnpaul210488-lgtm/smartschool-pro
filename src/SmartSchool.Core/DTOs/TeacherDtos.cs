namespace SmartSchool.Core.DTOs;

public sealed record TeacherListItem(
    int Id,
    string FullName,
    string Phone,
    string? Subject,
    int? UserId,
    string? Username,
    bool IsActive,
    IReadOnlyList<string> Classes);

public class TeacherCreateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Subject { get; set; }

    /// <summary>Optional: creates a Teacher login (Role=Teacher) linked to this profile.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class TeacherUpdateRequest : TeacherCreateRequest
{
    public bool IsActive { get; set; } = true;
}
