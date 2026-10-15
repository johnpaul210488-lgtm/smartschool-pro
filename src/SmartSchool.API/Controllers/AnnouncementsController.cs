using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

/// <summary>School announcements: everyone reads; only staff publishes (also broadcasts SignalR + FCM).</summary>
[ApiController]
[Authorize]
[Route("api/announcements")]
public sealed class AnnouncementsController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly ISchoolNotificationService _notify;
    private readonly IFcmSender _fcm;

    public AnnouncementsController(IUnitOfWork uow, ISchoolNotificationService notify, IFcmSender fcm)
    {
        _uow = uow;
        _notify = notify;
        _fcm = fcm;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AnnouncementListItem>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _uow.Announcements.Query().Where(a => a.IsActive);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AnnouncementListItem(
                a.Id, a.Title, a.Body, a.CreatedByName,
                PkTime.ToPk(a.CreatedAtUtc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture)))
            .ToListAsync(ct);

        return OkEnvelope(PagedResult<AnnouncementListItem>.Create(items, page, pageSize, total));
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<AnnouncementListItem>>> Create(
        AnnouncementUpsertRequest request, CancellationToken ct)
    {
        var title = request.Title.Trim();
        var body = request.Body.Trim();
        if (title.Length is < 3 or > 200) return BadRequestEnvelope("Title must be 3-200 characters.");
        if (body.Length is < 3 or > 2000) return BadRequestEnvelope("Body must be 3-2000 characters.");

        var author = User.FindFirst("fullName")?.Value ?? User.Identity?.Name ?? "Admin";
        var announcement = new Announcement
        {
            Title = title, Body = body, ClassId = request.ClassId, CreatedByName = author
        };
        await _uow.Announcements.AddAsync(announcement, ct);
        await _uow.SaveChangesAsync(ct);

        var timeText = PkTime.NowPk.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture);

        // Real-time broadcast (best-effort; the row is already durable).
        try
        {
            await _notify.SendAnnouncementAsync(
                new AnnouncementNotification(announcement.Id, title, body, timeText), ct);
        }
        catch { /* SignalR outage must not fail the POST */ }

        try
        {
            var tokens = await _uow.DeviceTokens.Query()
                .Where(d => d.User.Role == Shared.Enums.UserRole.Parent)
                .Select(d => d.Token)
                .ToListAsync(ct);
            await _fcm.SendToDevicesAsync(tokens,
                new FcmPush($"📢 {title}", body.Length > 120 ? body[..120] + "…" : body,
                    new Dictionary<string, string> { ["type"] = "announcement", ["id"] = announcement.Id.ToString() }),
                ct);
        }
        catch { /* push is best-effort */ }

        return OkEnvelope(new AnnouncementListItem(announcement.Id, title, body, author, timeText),
            "Announcement published.");
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct)
    {
        var announcement = await _uow.Announcements.FindAsync(new object[] { id }, ct);
        if (announcement is null) return NotFoundEnvelope($"Announcement {id} was not found.");
        announcement.IsActive = false; // soft delete keeps history
        _uow.Announcements.Update(announcement);
        await _uow.SaveChangesAsync(ct);
        return OkEnvelope(new { deleted = true }, "Announcement removed.");
    }
}
