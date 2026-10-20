namespace SmartSchool.Core.DTOs;

public sealed record StudentListItem(
    int Id,
    string FullName,
    string RollNo,
    string ClassName,
    int ClassId,
    string? BusNumber,
    string FatherName,
    string FatherPhone,
    string? PhotoUrl,
    bool IsActive,
    string Gender,
    bool IsNewStudent);

public sealed record StudentRecentEvent(
    long Id,
    string Type,
    string TimePakistan,
    string BusNumber,
    string? Location);

public sealed record StudentDetailResponse(
    int Id,
    string FullName,
    DateOnly DateOfBirth,
    string RollNo,
    int ClassId,
    string ClassName,
    string? BusNumber,
    int? BusId,
    string FatherName,
    string FatherPhone,
    string? MotherPhone,
    string Address,
    string? PhotoUrl,
    bool IsActive,
    string Gender,
    bool IsNewStudent,
    decimal TotalOutstandingFees,
    IReadOnlyList<StudentRecentEvent> RecentBusEvents);

public class StudentCreateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty; // "Boy" / "Girl" / ""
    public bool IsNewStudent { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public int ClassId { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string FatherName { get; set; } = string.Empty;
    public string FatherPhone { get; set; } = string.Empty;
    public string? MotherPhone { get; set; }
    public string Address { get; set; } = string.Empty;
    public int? BusId { get; set; }
}

public sealed class StudentUpdateRequest : StudentCreateRequest
{
    public bool IsActive { get; set; } = true;
}
