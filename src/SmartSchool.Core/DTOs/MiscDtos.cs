namespace SmartSchool.Core.DTOs;

public sealed record WhatsAppLogListItem(
    long Id,
    int? StudentId,
    string? StudentName,
    string ParentPhone,
    string Message,
    string Status,
    int RetryCount,
    string? Error,
    string CreatedAtPakistan,
    string? SentAtPakistan);

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
