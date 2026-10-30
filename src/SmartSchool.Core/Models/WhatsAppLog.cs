using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>
/// Durable provider-neutral outbox row for every WhatsApp alert, written before
/// the first send attempt. Upgrade migration explicitly retires unfinished legacy rows.
/// </summary>
public sealed class WhatsAppLog
{
    public long Id { get; set; }

    /// <summary>Dedupe key linking the queued job to this row.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Nullable so audit history survives a (hard) student delete.</summary>
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    /// <summary>
    /// Durable link to the exact driver tap that produced this alert. Keeping it
    /// in the outbox means restart/retry sends can still update attendance audit state.
    /// </summary>
    public long? BusAttendanceId { get; set; }
    public BusAttendance? BusAttendance { get; set; }

    /// <summary>"PickIn" | "DropOut" — used by the 30s debounce / rate limiter.</summary>
    public string? EventType { get; set; }

    public string ParentPhone { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public WhatsAppStatus Status { get; set; } = WhatsAppStatus.Queued;

    public int RetryCount { get; set; }

    public string? Error { get; set; }

    /// <summary>Provider identifier, if known; submission is not proof of delivery.</summary>
    public string? ProviderMessageId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAtUtc { get; set; }

    /// <summary>
    /// Start of the current short retry window. Null uses CreatedAtUtc; a human
    /// retry sets a new window without rewriting the immutable audit creation time.
    /// </summary>
    public DateTime? RetryWindowStartedAtUtc { get; set; }

    /// <summary>
    /// Durable retry schedule. Null means the row is ready now; a future value
    /// survives process restarts without an in-memory delay task.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>Stable machine-readable failure category (never credentials/provider payloads).</summary>
    public string? FailureCode { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
