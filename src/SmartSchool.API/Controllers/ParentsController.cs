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

/// <summary>
/// Parent app home endpoints: children cards (photo/class/bus/today status),
/// settings (language, WhatsApp opt-in, phone).
/// </summary>
[ApiController]
[Authorize(Roles = AppRoles.Parent)]
[Route("api/parents")]
public sealed class ParentsController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    private readonly SmartSchool.API.Services.LiveBusTracker _live;

    public ParentsController(IUnitOfWork uow, SmartSchool.API.Services.LiveBusTracker live)
    {
        _uow = uow;
        _live = live;
    }

    /// <summary>Parent ke bachon ki buses jo abhi active trip par hain: aakhri location (map khulte hi).</summary>
    [HttpGet("me/live-buses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SmartSchool.Core.DTOs.BusLocationUpdate>>>> GetLiveBuses(CancellationToken ct)
    {
        var studentIds = await _uow.ParentStudents.Query()
            .Where(ps => ps.ParentUserId == CallerUserId)
            .Select(ps => ps.StudentId)
            .ToListAsync(ct);
        if (studentIds.Count == 0)
            return OkEnvelope<IReadOnlyList<SmartSchool.Core.DTOs.BusLocationUpdate>>(Array.Empty<SmartSchool.Core.DTOs.BusLocationUpdate>());

        var busIds = await _uow.Students.Query()
            .Where(s => studentIds.Contains(s.Id) && s.BusId != null)
            .Select(s => s.BusId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var activeBusIds = await _uow.Trips.Query()
            .Where(t => t.Status == TripStatus.Active && busIds.Contains(t.BusId))
            .Select(t => t.BusId)
            .ToListAsync(ct);

        return OkEnvelope<IReadOnlyList<SmartSchool.Core.DTOs.BusLocationUpdate>>(_live.Snapshot(activeBusIds));
    }

    /// <summary>Summary sentence per status, localized for the app.</summary>
    public static readonly IReadOnlyDictionary<string, (string En, string Ur)> StatusText =
        new Dictionary<string, (string, string)>
        {
            ["NotBoarded"] = ("Not boarded yet", "Abhi bus par nahi sawar"),
            ["OnBus"] = ("On the bus", "Bus par sawar hai"),
            ["AtSchool"] = ("Reached school", "School pahunch chuka hai"),
            ["DroppedHome"] = ("Dropped home", "Ghar pahunch diya gaya hai")
        };

    [HttpGet("me/children")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParentChildCardDto>>>> GetMyChildren(CancellationToken ct)
    {
        var links = await _uow.ParentStudents.Query()
            .Where(ps => ps.ParentUserId == CallerUserId)
            .Select(ps => ps.StudentId)
            .ToListAsync(ct);
        if (links.Count == 0)
            return OkEnvelope(Array.Empty<ParentChildCardDto>());

        var children = await _uow.Students.Query()
            .Where(s => links.Contains(s.Id) && s.IsActive)
            .Select(s => new
            {
                s.Id, s.FullName, s.PhotoUrl,
                ClassName = s.Class.Name + "-" + s.Class.Section,
                s.BusId,
                BusNumber = s.Bus != null ? s.Bus.BusNumber : null
            })
            .ToListAsync(ct);

        var today = PkTime.TodayPk;
        var startUtc = PkTime.PkDayStartUtc(today);
        var endUtc = PkTime.PkDayEndUtc(today);

        var events = await _uow.BusAttendances.Query()
            .Where(e => links.Contains(e.StudentId) && e.TimestampUtc >= startUtc && e.TimestampUtc < endUtc)
            .GroupBy(e => e.StudentId)
            .Select(g => g.OrderByDescending(e => e.TimestampUtc)
                .Select(e => new { e.StudentId, e.Type, e.TimestampUtc, TripType = e.Trip != null ? (TripType?)e.Trip.Type : null })
                .First())
            .ToListAsync(ct);

        var lang = (User.FindFirst("preferredLanguage")?.Value ?? "en") == "ur" ? "ur" : "en";

        var cards = children.Select(c =>
        {
            var last = events.FirstOrDefault(e => e.StudentId == c.Id);
            string status;
            string? lastText = null, lastTime = null;

            if (last is null)
            {
                status = "NotBoarded";
            }
            else
            {
                var pk = PkTime.ToPk(last.TimestampUtc);
                status = last.Type == BusAttendanceType.PickIn
                    ? "OnBus"
                    : last.TripType switch
                    {
                        TripType.ToSchool => "AtSchool",
                        TripType.ToHome => "DroppedHome",
                        _ => pk.Hour < 12 ? "AtSchool" : "DroppedHome" // no linked trip: time heuristic
                    };
                lastTime = pk.ToString("hh:mm tt", CultureInfo.InvariantCulture);
            }

            var (en, ur) = StatusText[status];
            lastText = lang == "ur" ? ur : en;

            return new ParentChildCardDto(
                c.Id, c.FullName, c.PhotoUrl, c.ClassName, c.BusId, c.BusNumber,
                status, lastText, lastTime);
        }).ToList();

        return OkEnvelope(cards);
    }

    [HttpGet("me/settings")]
    public async Task<ActionResult<ApiResponse<ParentSettingsDto>>> GetSettings(CancellationToken ct)
    {
        var me = await _uow.Users.FindAsync(new object[] { CallerUserId }, ct);
        if (me is null) return NotFoundEnvelope("User not found.");
        return OkEnvelope(new ParentSettingsDto(me.Username, me.Phone, me.PreferredLanguage, me.ReceiveWhatsApp));
    }

    [HttpPatch("me/settings")]
    public async Task<ActionResult<ApiResponse<ParentSettingsDto>>> UpdateSettings(
        UpdateParentSettingsRequest request, CancellationToken ct)
    {
        var me = await _uow.Users.FindAsync(new object[] { CallerUserId }, ct);
        if (me is null) return NotFoundEnvelope("User not found.");

        if (request.Phone is not null)
        {
            var phone = request.Phone.Trim();
            if (phone.Length > 0 && (phone.Length < 10 || phone.Length > 25))
                return BadRequestEnvelope("Phone number must be 10-25 characters (e.g. 03001234567).");
            me.Phone = phone.Length == 0 ? null : phone;
        }

        if (request.PreferredLanguage is not null)
        {
            var lang = request.PreferredLanguage.Trim().ToLowerInvariant();
            if (lang is not ("en" or "ur"))
                return BadRequestEnvelope("Language must be \"en\" or \"ur\".");
            me.PreferredLanguage = lang;
        }

        if (request.ReceiveWhatsApp is { } wa)
            me.ReceiveWhatsApp = wa;

        _uow.Users.Update(me);
        await _uow.SaveChangesAsync(ct);
        return OkEnvelope(new ParentSettingsDto(me.Username, me.Phone, me.PreferredLanguage, me.ReceiveWhatsApp),
            "Settings saved.");
    }
}
