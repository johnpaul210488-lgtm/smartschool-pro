namespace SmartSchool.Core.DTOs;

public sealed record WhatsAppLogListItem(
    long Id,
    int? StudentId,
    string? StudentName,
    long? BusAttendanceId,
    string? EventType,
    string ParentPhone,
    string Message,
    string Status,
    string? ProviderMessageId,
    int RetryCount,
    string? FailureCode,
    string? Error,
    string CreatedAtPakistan,
    string? SentAtPakistan,
    string? NextAttemptPakistan);

/// <summary>Everything the WPF admin dashboard home screen needs in one call.</summary>
public sealed record DashboardSummary(
    string DatePakistan,
    int ActiveStudents,
    int TotalTeachers,
    int TotalBuses,
    int TotalDrivers,
    int ActiveTripsNow,
    int PickInsToday,
    int DropOutsToday,
    int PendingFeesCount,
    decimal PendingFeesAmount,
    decimal CollectedThisMonth,
    int WhatsAppSentToday,
    int WhatsAppFailedTotal);

// ============ Error / bug reports ============
/// <summary>Admin/parent/driver app se ata hai — server GitHub Issue banane ki koshish karta hai.</summary>
public sealed class ErrorReportCreateRequest
{
    public string Source { get; set; } = "Admin";      // Admin / Parent / Driver / API
    public string Severity { get; set; } = "Error";    // Info / Warning / Error / Crash
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? ContextJson { get; set; }
}

public sealed record ErrorReportCreatedResponse(int Id, int? IssueNumber, int Occurrences);

public sealed record ErrorReportListItem(
    int Id,
    string Source,
    string Severity,
    string Title,
    string Message,
    int Occurrences,
    int? IssueNumber,
    bool IsResolved,
    string CreatedAtPakistan,
    string LastSeenAtPakistan);
