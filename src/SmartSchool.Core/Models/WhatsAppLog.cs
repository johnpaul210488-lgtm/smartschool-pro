using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>
/// Durable outbox row for every WhatsApp message: written BEFORE the first
/// send attempt so a crash or Twilio outage never loses a message - the
/// background queue retries Queued rows with backoff.
/// </summary>
public sealed class WhatsAppLog
{
    public long Id { get; set; }

    /// <summary>Dedupe key linking the queued job to this row.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Nullable so audit history survives a (hard) student delete.</summary>
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public string ParentPhone { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public WhatsAppStatus Status { get; set; } = WhatsAppStatus.Queued;

    public int RetryCount { get; set; }

    public string? Error { get; set; }

    /// <summary>Twilio SID (or DRYRUN-* in dev mode).</summary>
    public string? ProviderMessageId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
