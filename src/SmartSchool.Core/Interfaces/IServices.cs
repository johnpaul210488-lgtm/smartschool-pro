using SmartSchool.Core.DTOs;
using SmartSchool.Core.Models;

namespace SmartSchool.Core.Interfaces;

public interface IJwtTokenService
{
    string CreateToken(User user, out DateTime expiresAtUtc);
}

public sealed record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error)
{
    public static WhatsAppSendResult Ok(string providerId) => new(true, providerId, null);
    public static WhatsAppSendResult Fail(string error) => new(false, null, error);
}

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct = default);
}

public sealed record WhatsAppJob(
    Guid CorrelationId,
    long? BusAttendanceId,
    int? StudentId,
    string ParentPhone,
    string Message);

public interface IWhatsAppQueue
{
    ValueTask EnqueueAsync(WhatsAppJob job, CancellationToken ct = default);
}

public interface IGeocodingService
{
    /// <summary>Human-readable address for a GPS point; never throws, always has a fallback.</summary>
    Task<string> GetLocationLabelAsync(double lat, double lng, CancellationToken ct = default);
}

/// <summary>Server-side SignalR fan-out. Implemented in the API layer (web concern).</summary>
public interface ISchoolNotificationService
{
    Task SendPickInAlertAsync(BusEventNotification notification, CancellationToken ct = default);
    Task SendDropOutAlertAsync(BusEventNotification notification, CancellationToken ct = default);
    Task SendTripUpdateAsync(TripNotification notification, CancellationToken ct = default);
    Task SendBusLocationAsync(BusLocationUpdate update, CancellationToken ct = default);
    Task SendAdminAlertAsync(AdminAlertNotification alert, CancellationToken ct = default);
    /// <summary>School announcement → every admin desktop + every connected parent app.</summary>
    Task SendAnnouncementAsync(AnnouncementNotification announcement, CancellationToken ct = default);
}

// ============ Push notifications (FCM) ============

public sealed record FcmPush(string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Firebase Cloud Messaging sender. Firebase:Enabled=false = dry-run (logged only),
/// so the full chain is testable before the school creates a Firebase project.
/// </summary>
public interface IFcmSender
{
    /// <summary>Sends to the given device tokens. Returns the count that FCM accepted.</summary>
    Task<int> SendToDevicesAsync(IReadOnlyList<string> tokens, FcmPush push, CancellationToken ct = default);
}
