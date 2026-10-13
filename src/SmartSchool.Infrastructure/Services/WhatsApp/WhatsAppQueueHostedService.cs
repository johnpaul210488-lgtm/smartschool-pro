using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>In-memory channel the controllers publish to. Durability lives in WhatsAppLogs.</summary>
public sealed class InMemoryWhatsAppQueue : IWhatsAppQueue
{
    private readonly Channel<WhatsAppJob> _channel = Channel.CreateUnbounded<WhatsAppJob>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask EnqueueAsync(WhatsAppJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<WhatsAppJob> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Background consumer for WhatsApp alerts:
///  - Twilio down? Retry with exponential backoff (5s -> 30s -> 2min, max N attempts).
///  - Restart-safe: rows sit Queued in WhatsAppLogs; a sweeper re-enqueues stuck rows.
///  - Deduped by CorrelationId so a re-enqueued job can never double-send.
/// </summary>
public sealed class WhatsAppQueueHostedService : BackgroundService
{
    private readonly InMemoryWhatsAppQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhatsAppQueueHostedService> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _sweepInterval;

    public WhatsAppQueueHostedService(
        InMemoryWhatsAppQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<WhatsAppQueueHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxRetries = config.GetValue("WhatsAppQueue:MaxRetries", 3);
        _sweepInterval = TimeSpan.FromSeconds(config.GetValue("WhatsAppQueue:SweepIntervalSeconds", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            ProcessJobsAsync(stoppingToken),
            SweepStuckMessagesAsync(stoppingToken));
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
                    _logger.LogError(ex, "Error processing WhatsApp job {Id}", job.CorrelationId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task HandleJobAsync(WhatsAppJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();

        var log = await db.WhatsAppLogs.FirstOrDefaultAsync(l => l.CorrelationId == job.CorrelationId, ct);
        if (log is null)
        {
            log = new WhatsAppLog
            {
                CorrelationId = job.CorrelationId,
                StudentId = job.StudentId,
                ParentPhone = job.ParentPhone,
                Message = job.Message,
                Status = WhatsAppStatus.Queued,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.WhatsAppLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }

        // Already delivered by a previous attempt/duplicate job -> do not send twice.
        if (log.Status == WhatsAppStatus.Sent)
            return;

        log.LastAttemptAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var result = await sender.SendAsync(job.ParentPhone, job.Message, ct);

        if (result.Success)
        {
            log.Status = WhatsAppStatus.Sent;
            log.SentAtUtc = DateTime.UtcNow;
            log.ProviderMessageId = result.ProviderMessageId;
            log.Error = null;

            if (job.BusAttendanceId is { } attendanceId)
            {
                var attendance = await db.BusAttendances.FindAsync(new object?[] { attendanceId }, ct);
                if (attendance is not null)
                    attendance.WhatsAppSent = true;
            }

            await db.SaveChangesAsync(ct);
            return;
        }

        log.RetryCount += 1;
        log.Error = result.Error is { Length: > 1000 } ? result.Error[..1000] : result.Error;

        if (log.RetryCount >= _maxRetries)
        {
            log.Status = WhatsAppStatus.Failed;
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("WhatsApp message {Id} failed permanently after {N} attempts: {Error}",
                job.CorrelationId, log.RetryCount, result.Error);
            return;
        }

        log.Status = WhatsAppStatus.Queued;
        await db.SaveChangesAsync(ct);

        // Delayed re-enqueue WITHOUT blocking the reader loop.
        var delay = Backoff(log.RetryCount);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                await _queue.EnqueueAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-enqueue WhatsApp job {Id}", job.CorrelationId);
            }
        });
    }

    private static TimeSpan Backoff(int retryCount) => retryCount switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromMinutes(2)
    };

    /// <summary>Feeds messages stuck since before a restart (or manually reset) back into the channel.</summary>
    private async Task SweepStuckMessagesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_sweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

                var stuckCutoff = DateTime.UtcNow.AddMinutes(-2);
                var stuck = await db.WhatsAppLogs
                    .Where(l => l.Status == WhatsAppStatus.Queued
                                && l.RetryCount < _maxRetries
                                && (l.LastAttemptAtUtc == null || l.LastAttemptAtUtc < stuckCutoff))
                    .OrderBy(l => l.CreatedAtUtc)
                    .Take(50)
                    .ToListAsync(ct);

                foreach (var log in stuck)
                    await _queue.EnqueueAsync(
                        new WhatsAppJob(log.CorrelationId, null, log.StudentId, log.ParentPhone, log.Message), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
