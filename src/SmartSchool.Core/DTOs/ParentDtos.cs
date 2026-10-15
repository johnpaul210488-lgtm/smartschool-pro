namespace SmartSchool.Core.DTOs;

// ============ Parent app endpoints ============

public sealed record ParentChildCardDto(
    int StudentId,
    string FullName,
    string? PhotoUrl,
    string ClassName,
    int? BusId,
    string? BusNumber,
    /// <summary>NotBoarded | OnBus | AtSchool | DroppedHome</summary>
    string TodayStatus,
    string? LastEventText,
    string? LastEventTimePakistan);

public sealed record ParentSettingsDto(
    string Username,
    string? Phone,
    string PreferredLanguage,
    bool ReceiveWhatsApp);

public sealed class UpdateParentSettingsRequest
{
    public string? Phone { get; set; }
    /// <summary>"en" | "ur"</summary>
    public string? PreferredLanguage { get; set; }
    public bool? ReceiveWhatsApp { get; set; }
}

public sealed class RegisterDeviceRequest
{
    public required string Token { get; set; }
    /// <summary>android | ios</summary>
    public string Platform { get; set; } = "android";
}

// ============ Fee receipt (rendered/printed client-side or in WPF) ============

public sealed record FeeReceiptDto(
    string ReceiptNo,
    string StudentName,
    string ClassName,
    string Month,
    decimal Amount,
    DateOnly DueDate,
    DateOnly PaidDate,
    string PaymentDatePakistan,
    string? Notes);

// ============ Announcements ============

public sealed record AnnouncementListItem(
    int Id,
    string Title,
    string Body,
    string? CreatedByName,
    string TimePakistan);

public sealed record AnnouncementNotification(
    int Id,
    string Title,
    string Body,
    string TimePakistan);

public sealed class AnnouncementUpsertRequest
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public int? ClassId { get; set; }
}
