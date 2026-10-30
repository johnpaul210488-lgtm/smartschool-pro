using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>
/// Bounded in-memory wake-up lane. The database row is the durable source of
/// truth; this channel contains only correlation IDs currently scheduled in this process.
/// </summary>
public sealed class InMemoryWhatsAppQueue : IWhatsAppQueue
{
    private readonly Channel<WhatsAppJob> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _scheduled = new();

    public InMemoryWhatsAppQueue(IOptions<WhatsAppOptions> options)
    {
        var capacity = Math.Clamp(options.Value.Queue.Capacity, 1, 100_000);
        _channel = Channel.CreateBounded<WhatsAppJob>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public async ValueTask EnqueueAsync(WhatsAppJob job, CancellationToken ct = default)
    {
        if (!_scheduled.TryAdd(job.CorrelationId, 0)) return;
        try
        {
            await _channel.Writer.WriteAsync(job, ct);
        }
        catch
        {
            _scheduled.TryRemove(job.CorrelationId, out _);
            throw;
        }
    }

    public IAsyncEnumerable<WhatsAppJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public void Complete(Guid correlationId) => _scheduled.TryRemove(correlationId, out _);
}

/// <summary>
/// Restart-safe WhatsApp outbox:
///  - one serial consumer, bounded wake-up queue and correlation-ID deduplication;
///  - durable NextAttemptAtUtc scheduling (no orphaned Task.Run delay);
///  - at most three total attempts inside a short two-minute retry window;
///  - permanent/ambiguous failures never auto-repeat and always alert an admin.
/// Provider-specific pacing and idempotency are enforced again by the linked-device gateway.
/// </summary>
public sealed class WhatsAppQueueHostedService : BackgroundService
{
    private const string HandoffInProgress = "handoff_in_progress";

    private readonly InMemoryWhatsAppQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppQueueHostedService> _logger;
    private readonly WhatsAppQueueOptions _options;
    private readonly TimeSpan _sweepInterval;

    public WhatsAppQueueHostedService(
        InMemoryWhatsAppQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<WhatsAppOptions> options,
        ILogger<WhatsAppQueueHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value.Queue;
        _options.Capacity = Math.Clamp(_options.Capacity, 1, 100_000);
        _options.MaxAttempts = Math.Clamp(_options.MaxAttempts, 1, 10);
        _options.RetryWindowSeconds = Math.Clamp(_options.RetryWindowSeconds, 30, 3_600);
        _options.InitialRetryDelaySeconds = Math.Clamp(_options.InitialRetryDelaySeconds, 1, 600);
        _options.SweepIntervalSeconds = Math.Clamp(_options.SweepIntervalSeconds, 1, 300);
        _sweepInterval = TimeSpan.FromSeconds(_options.SweepIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            ProcessJobsAsync(stoppingToken),
            SweepDueMessagesAsync(stoppingToken));
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _queue.ReadAllAsync(ct))
            {
                try
                {
                    await HandleJobAsync(job, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected WhatsApp outbox error for {CorrelationId}", job.CorrelationId);
                }
                finally
                {
                    _queue.Complete(job.CorrelationId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Processes one durable row; public for deterministic integration tests.</summary>
    public Task ProcessAsync(Guid correlationId, CancellationToken ct = default)
        => HandleJobAsync(new WhatsAppJob(correlationId, null, null, string.Empty, string.Empty), ct);

    private async Task HandleJobAsync(WhatsAppJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();

        var log = await db.WhatsAppLogs.FirstOrDefaultAsync(l => l.CorrelationId == job.CorrelationId, ct);
        if (log is null)
        {
            _logger.LogWarning("WhatsApp wake-up {CorrelationId} has no durable outbox row; ignored.", job.CorrelationId);
            return;
        }

        // Compatibility/audit states from earlier QR builds are deliberately inert.
        // Only an explicitly eligible Queued row can cross a provider boundary.
        if (log.Status != WhatsAppStatus.Queued)
            return;
        if (string.Equals(log.FailureCode, HandoffInProgress, StringComparison.Ordinal))
        {
            await FailPermanentlyAsync(
                log,
                WhatsAppFailureKind.DeliveryUnknown.ToString(),
                "A previous provider handoff was interrupted. Delivery is unknown, so the alert was not sent again.",
                scope,
                db,
                ct);
            return;
        }
        if (log.NextAttemptAtUtc is { } notBefore && notBefore > DateTime.UtcNow)
            return;

        // Consent and the student's current recipient number are revalidated at the
        // final boundary so a revocation made after enqueue still stops the send.
        if (!await IsRecipientAllowedAsync(db, log, ct))
        {
            log.Status = WhatsAppStatus.Held;
            log.FailureCode = "consent_required";
            log.Error = "Explicit permission is missing/revoked, the parent opted out, or the student's number changed.";
            log.NextAttemptAtUtc = null;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("WhatsApp alert {CorrelationId} held by the consent gate.", log.CorrelationId);
            return;
        }

        var now = DateTime.UtcNow;
        var retryWindowStart = log.RetryWindowStartedAtUtc ?? log.CreatedAtUtc;
        var retryDeadline = retryWindowStart.AddSeconds(_options.RetryWindowSeconds);
        if (now > retryDeadline)
        {
            await FailPermanentlyAsync(
                log,
                "retry_window_expired",
                "The alert expired before a safe send could be confirmed; it was not sent late.",
                scope,
                db,
                ct);
            return;
        }

        // Atomically claim the row immediately before crossing the provider boundary.
        // This prevents two API processes from handing off the same alert. A process
        // interruption leaves Sending + handoff_in_progress, which recovery fails
        // closed as DeliveryUnknown instead of risking a duplicate.
        log = await ClaimForHandoffAsync(db, log.CorrelationId, now, ct);
        if (log is null) return;

        WhatsAppSendResult result;
        try
        {
            result = await sender.SendAsync(log.CorrelationId, log.ParentPhone, log.Message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp provider handoff threw for {CorrelationId}; delivery is unknown.", log.CorrelationId);
            result = WhatsAppSendResult.Fail(
                "The provider handoff ended unexpectedly. Delivery is unknown, so this alert will not be sent again.",
                WhatsAppFailureKind.DeliveryUnknown,
                retryable: false);
        }

        if (result.Success)
        {
            log.Status = WhatsAppStatus.Sent;
            log.SentAtUtc = DateTime.UtcNow;
            log.ProviderMessageId = result.ProviderMessageId;
            log.Error = null;
            log.FailureCode = null;
            log.NextAttemptAtUtc = null;

            // The event link is stored with the durable row, so a sweep/retry after
            // an API restart updates the same attendance audit record as an immediate send.
            if ((log.BusAttendanceId ?? job.BusAttendanceId) is { } attendanceId)
            {
                var attendance = await db.BusAttendances.FindAsync(new object?[] { attendanceId }, ct);
                if (attendance is not null) attendance.WhatsAppSent = true;
            }

            await db.SaveChangesAsync(ct);
            return;
        }

        log.RetryCount += 1;
        log.Error = Truncate(result.Error ?? "Unknown WhatsApp failure.", 1000);
        log.FailureCode = result.FailureKind.ToString();

        var exhausted = log.RetryCount >= _options.MaxAttempts;
        var retryDelay = RetryDelay(log.RetryCount, result.RetryAfterSeconds);
        var nextAttempt = DateTime.UtcNow.Add(retryDelay);
        if (!result.Retryable || exhausted || nextAttempt > retryDeadline)
        {
            await FailPermanentlyAsync(
                log,
                result.FailureKind.ToString(),
                log.Error,
                scope,
                db,
                ct);
            return;
        }

        log.Status = WhatsAppStatus.Queued;
        log.NextAttemptAtUtc = nextAttempt;
        await db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "WhatsApp {CorrelationId} attempt {Attempt}/{MaxAttempts} failed ({Failure}); next safe attempt at {NextAttemptUtc}.",
            log.CorrelationId, log.RetryCount, _options.MaxAttempts, result.FailureKind, nextAttempt);
    }

    private static async Task<WhatsAppLog?> ClaimForHandoffAsync(
        SchoolDbContext db,
        Guid correlationId,
        DateTime now,
        CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            var claimed = await db.WhatsAppLogs
                .Where(l => l.CorrelationId == correlationId
                            && l.Status == WhatsAppStatus.Queued
                            && (l.NextAttemptAtUtc == null || l.NextAttemptAtUtc <= now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(l => l.Status, WhatsAppStatus.Sending)
                    .SetProperty(l => l.LastAttemptAtUtc, now)
                    .SetProperty(l => l.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(l => l.FailureCode, HandoffInProgress)
                    .SetProperty(l => l.Error, (string?)null), ct);
            if (claimed != 1) return null;
            db.ChangeTracker.Clear();
            return await db.WhatsAppLogs.SingleAsync(l => l.CorrelationId == correlationId, ct);
        }

        // EF's in-memory provider is used only by deterministic unit tests.
        var item = await db.WhatsAppLogs.SingleOrDefaultAsync(
            l => l.CorrelationId == correlationId && l.Status == WhatsAppStatus.Queued, ct);
        if (item is null || item.NextAttemptAtUtc is { } due && due > now) return null;
        item.Status = WhatsAppStatus.Sending;
        item.LastAttemptAtUtc = now;
        item.NextAttemptAtUtc = null;
        item.FailureCode = HandoffInProgress;
        item.Error = null;
        await db.SaveChangesAsync(ct);
        return item;
    }

    private TimeSpan RetryDelay(int failedAttempts, int? providerRetryAfterSeconds)
    {
        var exponent = Math.Min(5, Math.Max(0, failedAttempts - 1));
        var localDelay = _options.InitialRetryDelaySeconds * (1 << exponent);
        var seconds = Math.Max(localDelay, providerRetryAfterSeconds ?? 0);
        return TimeSpan.FromSeconds(Math.Min(seconds, _options.RetryWindowSeconds));
    }

    private async Task FailPermanentlyAsync(
        WhatsAppLog log,
        string failureCode,
        string error,
        IServiceScope scope,
        SchoolDbContext db,
        CancellationToken ct)
    {
        log.Status = WhatsAppStatus.Failed;
        log.FailureCode = Truncate(failureCode, 40);
        log.Error = Truncate(error, 1000);
        log.NextAttemptAtUtc = null;
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "WhatsApp alert {CorrelationId} stopped after {Attempts} failed attempt(s): {FailureCode}",
            log.CorrelationId, log.RetryCount, log.FailureCode);

        try
        {
            var notify = scope.ServiceProvider.GetRequiredService<ISchoolNotificationService>();
            await notify.SendAdminAlertAsync(new SmartSchool.Core.DTOs.AdminAlertNotification
            {
                Title = "WhatsApp alert needs attention",
                Message = $"Student ({(log.StudentId?.ToString() ?? "?")}) → phone ending " +
                          $"{PhoneSuffix(log.ParentPhone)}: message was not confirmed. " +
                          $"Reason: {log.FailureCode}. Check WhatsApp Connection; call the parent if urgent.",
                TimePakistan = SmartSchool.Shared.Helpers.PkTime.NowPk
                    .ToString("dd MMM yyyy hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Admin SignalR alert failed; WhatsApp row remains Failed in the database.");
        }
    }

    private async Task SweepDueMessagesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
                var now = DateTime.UtcNow;
                var interruptedBefore = now.AddSeconds(-_options.RetryWindowSeconds);
                var interrupted = await db.WhatsAppLogs
                    .Where(l => l.Status == WhatsAppStatus.Sending
                                && l.FailureCode == HandoffInProgress
                                && l.LastAttemptAtUtc <= interruptedBefore)
                    .OrderBy(l => l.LastAttemptAtUtc)
                    .Take(100)
                    .ToListAsync(ct);
                foreach (var log in interrupted)
                {
                    await FailPermanentlyAsync(
                        log,
                        WhatsAppFailureKind.DeliveryUnknown.ToString(),
                        "The API stopped during provider handoff. Delivery is unknown, so the alert was not sent again.",
                        scope,
                        db,
                        ct);
                }

                var due = await db.WhatsAppLogs
                    .AsNoTracking()
                    .Where(l => l.Status == WhatsAppStatus.Queued
                                && l.RetryCount < _options.MaxAttempts
                                && (l.NextAttemptAtUtc == null || l.NextAttemptAtUtc <= now))
                    .OrderBy(l => l.CreatedAtUtc)
                    .Take(100)
                    .Select(l => new WhatsAppJob(
                        l.CorrelationId, l.BusAttendanceId, l.StudentId, l.ParentPhone, l.Message))
                    .ToListAsync(ct);

                foreach (var job in due)
                    await _queue.EnqueueAsync(job, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp durable outbox sweep failed; it will retry after the bounded interval.");
            }

            try
            {
                await Task.Delay(_sweepInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task<bool> IsRecipientAllowedAsync(
        SchoolDbContext db,
        WhatsAppLog log,
        CancellationToken ct = default)
    {
        if (log.StudentId is null || PhoneNormalizer.ToE164(log.ParentPhone) is not { } phone)
            return false;

        var student = await db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == log.StudentId && s.IsActive, ct);
        if (student is null
            || (phone != PhoneNormalizer.ToE164(student.FatherPhone)
                && phone != PhoneNormalizer.ToE164(student.MotherPhone)))
            return false;

        var consented = await db.WhatsAppConsents.AsNoTracking()
            .AnyAsync(c => c.StudentId == student.Id && c.PhoneE164 == phone && c.Granted, ct);
        if (!consented) return false;

        var matchingParentSettings = await db.ParentStudents.AsNoTracking()
            .Where(p => p.StudentId == student.Id)
            .Select(p => new { p.ParentUser.Phone, p.ParentUser.ReceiveWhatsApp })
            .ToListAsync(ct);
        return !matchingParentSettings.Any(
            p => PhoneNormalizer.ToE164(p.Phone) == phone && !p.ReceiveWhatsApp);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string PhoneSuffix(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }
}
