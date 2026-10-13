namespace SmartSchool.Core.DTOs;

public sealed record ClassListItem(
    int Id,
    string Name,
    string Section,
    string DisplayName,
    int? TeacherId,
    string? ClassTeacherName,
    int StudentCount);

public sealed class ClassUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public int? TeacherId { get; set; }
}
