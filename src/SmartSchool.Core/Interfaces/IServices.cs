using SmartSchool.Core.DTOs;
using SmartSchool.Core.Models;

namespace SmartSchool.Core.Interfaces;

public interface IJwtTokenService
{
    string CreateToken(User user, out DateTime expiresAtUtc);
}

// Submitted means handed to the provider, NOT confirmed delivered. Unknown
// must never be retried automatically because the first attempt may have sent.
public enum WhatsAppSendOutcome { NotSent, Submitted, Unknown }
public sealed record WhatsAppSendResult(WhatsAppSendOutcome Outcome, string? ProviderMessageId = null, string? Error = null);

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppLog log, CancellationToken ct = default);
}

/// <summary>Null until a staff member explicitly authorizes the linked QR account.</summary>
public interface IWhatsAppActivation
{
    DateTime? ActivatedAtUtc { get; }
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

    /// <summary>Fee due / overdue alert → linked parents of the student + admin desktops.</summary>
    Task SendFeeDueAlertAsync(FeeDueNotification notification, CancellationToken ct = default);
}

// ============ WhatsApp message service (Baileys Node.js gateway) ============

public sealed record WhatsAppMessageRequest(string To, string Text, int? Priority = null);
public sealed record WhatsAppMediaRequest(string To, string Type, string Path, string? FileName, string? Caption, int? Priority = null);
public sealed record WhatsAppSendResponse(bool Ok, string? Id, string? Status, string? Error);

/// <summary>
/// Sends WhatsApp messages via the local Baileys Node.js gateway (HTTP API).
/// Optional: disabled when SMARTSCHOOL_WHATSAPP_ENABLED=false.
/// </summary>
public interface IWhatsAppMessageService
{
    Task<bool> IsEnabled();
    Task<WhatsAppSendResponse> SendText(string to, string text, int? priority = null, CancellationToken ct = default);
    Task<WhatsAppSendResponse> SendMedia(string to, string type, string path, string? fileName, string? caption, int? priority = null, CancellationToken ct = default);
    Task<dynamic?> GetStatus(CancellationToken ct = default);
}

// ============ Push notifications (FCM) ============

public sealed record FcmPush(string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Firebase Cloud Messaging sender. Firebase:Enabled=false = dry-run (logged only;
/// no messages are delivered and the accepted count is zero).
/// </summary>
public interface IFcmSender
{
    /// <summary>Sends to the given device tokens. Returns the count that FCM accepted.</summary>
    Task<int> SendToDevicesAsync(IReadOnlyList<string> tokens, FcmPush push, CancellationToken ct = default);
}
