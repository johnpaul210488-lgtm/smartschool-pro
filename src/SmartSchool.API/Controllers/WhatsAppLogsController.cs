using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
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
                l.RetryCount, l.Error, l.CreatedAtUtc, l.SentAtUtc,
                StudentName = l.Student != null ? l.Student.FullName : null
            })
            .ToListAsync(ct);

        string Fmt(DateTime utc)
            => PkTime.ToPk(utc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture);

        var items = rows.Select(l => new WhatsAppLogListItem(
            l.Id, l.StudentId, l.StudentName, l.ParentPhone, l.Message,
            l.Status.ToString(), l.RetryCount, l.Error,
            Fmt(l.CreatedAtUtc), l.SentAtUtc is { } sent ? Fmt(sent) : null)).ToList();

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

        log.Status = WhatsAppStatus.Queued;
        log.RetryCount = 0;
        log.LastAttemptAtUtc = null;
        log.Error = null;
        await _uow.SaveChangesAsync(ct);

        await _queue.EnqueueAsync(
            new WhatsAppJob(log.CorrelationId, null, log.StudentId, log.ParentPhone, log.Message), ct);

        return OkMessage("Message re-queued for sending.");
    }
}
