using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/whatsapp-logs")]
[Authorize(Roles = AppRoles.Staff)]
public sealed class WhatsAppLogsController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly IWhatsAppQueue _queue;

    public WhatsAppLogsController(IUnitOfWork uow, IWhatsAppQueue queue)
    {
        _uow = uow;
        _queue = queue;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<WhatsAppLogListItem>>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] int? studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.WhatsAppLogs.Query().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<WhatsAppStatus>(status, true, out var parsed))
            query = query.Where(l => l.Status == parsed);
        if (studentId is not null)
            query = query.Where(l => l.StudentId == studentId);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id, l.StudentId, l.ParentPhone, l.Message, l.Status,
                l.RetryCount, l.FailureCode, l.Error, l.CreatedAtUtc, l.SentAtUtc, l.NextAttemptAtUtc,
                StudentName = l.Student != null ? l.Student.FullName : null
            })
            .ToListAsync(ct);

        string Fmt(DateTime utc)
            => PkTime.ToPk(utc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture);

        var items = rows.Select(l => new WhatsAppLogListItem(
            l.Id, l.StudentId, l.StudentName, l.ParentPhone, l.Message,
            l.Status.ToString(), l.RetryCount, l.FailureCode, l.Error,
            Fmt(l.CreatedAtUtc),
            l.SentAtUtc is { } sent ? Fmt(sent) : null,
            l.NextAttemptAtUtc is { } next ? Fmt(next) : null)).ToList();

        return OkEnvelope(PagedResult<WhatsAppLogListItem>.Create(items, page, pageSize, total));
    }

    /// <summary>Reset a failed message and push it back into the queue immediately.</summary>
    [HttpPost("{id:long}/retry")]
    public async Task<ActionResult> Retry(long id, CancellationToken ct)
    {
        var log = await _uow.WhatsAppLogs.FindAsync(new object[] { id }, ct);
        if (log is null)
            return NotFoundEnvelope($"WhatsApp log {id} was not found.");
        if (log.Status == WhatsAppStatus.Sent)
            return ConflictEnvelope("This message was already sent.");
        if (log.Status != WhatsAppStatus.Failed)
            return ConflictEnvelope("This message is already queued/held or being handed to the provider.");
        if (log.EventType is "PickIn" or "DropOut"
            && !WhatsAppOutboxRules.IsFresh(log.CreatedAtUtc, DateTime.UtcNow))
            return ConflictEnvelope("This time-sensitive bus alert is now stale and cannot be replayed. Contact the parent directly if needed.");
        if (log.FailureCode == "legacy_not_replayed")
            return ConflictEnvelope("Historical provider rows are audit-only and cannot be replayed. Create a fresh event if a new alert is required.");
        if (log.FailureCode == WhatsAppFailureKind.DeliveryUnknown.ToString())
            return ConflictEnvelope(
                "WhatsApp may already have accepted this message. Automatic/manual resend is blocked to avoid a duplicate; verify in the selected provider or linked phone first.");

        log.Status = WhatsAppStatus.Queued;
        log.RetryCount = 0;
        log.LastAttemptAtUtc = null;
        log.NextAttemptAtUtc = null;
        log.FailureCode = null;
        log.Error = null;
        // A human retry opens a new short window without rewriting CreatedAtUtc;
        // CorrelationId also stays stable so the provider can reconcile duplicates.
        log.RetryWindowStartedAtUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        await _queue.EnqueueAsync(
            new WhatsAppJob(log.CorrelationId, null, log.StudentId, log.ParentPhone, log.Message), ct);

        return OkMessage("Message re-queued for sending.");
    }
}
