using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>Durability is in WhatsAppLogs, not this wake-up channel.</summary>
public sealed class InMemoryWhatsAppQueue : IWhatsAppQueue
{
    private readonly Channel<WhatsAppJob> _channel = Channel.CreateUnbounded<WhatsAppJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<Guid, byte> _scheduled = new();
    public ValueTask EnqueueAsync(WhatsAppJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_scheduled.TryAdd(job.CorrelationId, 0)) return ValueTask.CompletedTask;
        if (_channel.Writer.TryWrite(job)) return ValueTask.CompletedTask;
        _scheduled.TryRemove(job.CorrelationId, out _);
        throw new InvalidOperationException("WhatsApp wake-up channel is closed.");
    }
    public void Complete(Guid id) => _scheduled.TryRemove(id, out _);
    public IAsyncEnumerable<WhatsAppJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Only newly-created PendingQr rows can be processed. Legacy Queued rows,
/// Held, Submitted and Unknown rows are never retried. Claim is atomic in SQL.
/// A crash during Sending becomes Unknown, not a duplicate send.
/// </summary>
public sealed class WhatsAppQueueHostedService(
    InMemoryWhatsAppQueue queue, IServiceScopeFactory scopes, IWhatsAppActivation activation,
    ILogger<WhatsAppQueueHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AttemptInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan UnknownAfter = TimeSpan.FromMinutes(2);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(ConsumeAsync(stoppingToken), SweepAsync(stoppingToken));

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(ct))
            {
                try { await ProcessAsync(job.CorrelationId, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "WhatsApp outbox processing failed for {CorrelationId}", job.CorrelationId); }
                finally { queue.Complete(job.CorrelationId); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>Internal for deterministic CI tests; input is only a wake-up ID.</summary>
    public async Task ProcessAsync(Guid id, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var now = DateTime.UtcNow;
        var claimable = db.WhatsAppLogs.Where(l => l.CorrelationId == id && l.Status == WhatsAppStatus.PendingQr &&
            (l.LastAttemptAtUtc == null || l.LastAttemptAtUtc < now - AttemptInterval));
        int claimed;
        if (db.Database.IsRelational())
        {
            claimed = await claimable.ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, WhatsAppStatus.Sending)
                .SetProperty(l => l.LastAttemptAtUtc, now), ct);
        }
        else
        {
            // InMemory exists only for isolated tests; real servers use SQL Server atomic claims.
            var item = await claimable.FirstOrDefaultAsync(ct);
            if (item is null) return;
            item.Status = WhatsAppStatus.Sending;
            item.LastAttemptAtUtc = now;
            claimed = await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        if (claimed != 1) return;
        var log = await db.WhatsAppLogs.SingleAsync(l => l.CorrelationId == id, ct);
        var routeSince = activation.ActivatedAtUtc;
        if (!WhatsAppOutboxRules.IsFresh(routeSince, log.CreatedAtUtc, DateTime.UtcNow))
        {
            log.Status = routeSince is null || log.CreatedAtUtc < routeSince ? WhatsAppStatus.Held : WhatsAppStatus.Expired;
            log.Error = "Routing disabled, changed, or alert expired; not sent.";
        }
        else if (!await IsRecipientAllowedAsync(db, log, ct))
        {
            log.Status = WhatsAppStatus.Held;
            log.Error = "Explicit consent missing/revoked, parent opted out, or recipient changed.";
        }
        else
        {
            var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
            WhatsAppSendResult result;
            try { result = await sender.SendAsync(log, ct); }
            catch (Exception ex)
            {
                logger.LogWarning("WhatsApp send outcome unknown ({ErrorType})", ex.GetType().Name);
                result = new(WhatsAppSendOutcome.Unknown, Error: "Outcome unknown; do not retry automatically.");
            }
            log.RetryCount++;
            log.Error = result.Error is { Length: > 1000 } ? result.Error[..1000] : result.Error;
            switch (result.Outcome)
            {
                case WhatsAppSendOutcome.Submitted:
                    log.Status = WhatsAppStatus.Submitted;
                    log.ProviderMessageId = result.ProviderMessageId;
                    // Neither SentAtUtc nor BusAttendance.WhatsAppSent is set: there is no delivery receipt.
                    break;
                case WhatsAppSendOutcome.NotSent:
                    log.Status = WhatsAppOutboxRules.IsFresh(activation.ActivatedAtUtc, log.CreatedAtUtc, DateTime.UtcNow)
                        ? WhatsAppStatus.PendingQr : WhatsAppStatus.Expired;
                    break;
                default:
                    log.Status = WhatsAppStatus.Unknown;
                    break;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task<bool> IsRecipientAllowedAsync(SchoolDbContext db, WhatsAppLog log, CancellationToken ct = default)
    {
        if (log.StudentId is null || PhoneNormalizer.ToE164(log.ParentPhone) is not { } phone) return false;
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == log.StudentId && s.IsActive, ct);
        if (student is null || (phone != PhoneNormalizer.ToE164(student.FatherPhone) &&
                                phone != PhoneNormalizer.ToE164(student.MotherPhone))) return false;
        if (!await db.WhatsAppConsents.AsNoTracking().AnyAsync(c => c.StudentId == student.Id && c.PhoneE164 == phone && c.Granted, ct)) return false;
        var parents = await db.ParentStudents.AsNoTracking().Where(p => p.StudentId == student.Id)
            .Select(p => new { p.ParentUser.Phone, p.ParentUser.ReceiveWhatsApp }).ToListAsync(ct);
        return !parents.Any(p => PhoneNormalizer.ToE164(p.Phone) == phone && !p.ReceiveWhatsApp);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
                var deadline = DateTime.UtcNow - UnknownAfter;
                if (db.Database.IsRelational())
                    await db.WhatsAppLogs.Where(l => l.Status == WhatsAppStatus.Sending && l.LastAttemptAtUtc < deadline)
                        .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, WhatsAppStatus.Unknown)
                            .SetProperty(l => l.Error, "Interrupted attempt; outcome unknown. Do not retry automatically."), ct);
                else
                {
                    var interrupted = await db.WhatsAppLogs.Where(l => l.Status == WhatsAppStatus.Sending && l.LastAttemptAtUtc < deadline).ToListAsync(ct);
                    foreach (var l in interrupted) { l.Status = WhatsAppStatus.Unknown; l.Error = "Interrupted attempt; outcome unknown."; }
                    await db.SaveChangesAsync(ct);
                }
                // Only the new status can enter the QR queue, never old Queued rows.
                var ids = await db.WhatsAppLogs.AsNoTracking().Where(l => l.Status == WhatsAppStatus.PendingQr &&
                    (l.LastAttemptAtUtc == null || l.LastAttemptAtUtc < DateTime.UtcNow - AttemptInterval))
                    .OrderBy(l => l.CreatedAtUtc).Take(100).Select(l => l.CorrelationId).ToListAsync(ct);
                foreach (var id in ids) await queue.EnqueueAsync(new WhatsAppJob(id, null, null, "", ""), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "WhatsApp outbox sweep failed"); }
            try { await Task.Delay(SweepInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
