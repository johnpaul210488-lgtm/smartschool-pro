using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

/// <summary>
/// THE critical endpoints of the whole system. Driver taps PICK-IN / DROP-OUT ->
/// (1) event persisted, (2) SignalR alert to AdminGroup + ParentGroup_{studentId},
/// (3) father's WhatsApp message queued (durable outbox, retries if Twilio is down).
/// </summary>
[ApiController]
[Route("api/bus-attendance")]
public sealed class BusAttendanceController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly IWhatsAppQueue _whatsAppQueue;
    private readonly ISchoolNotificationService _notify;
    private readonly IGeocodingService _geocoding;
    private readonly IFcmSender _fcm;
    private readonly IConfiguration _config;
    private readonly ILogger<BusAttendanceController> _logger;

    public BusAttendanceController(
        IUnitOfWork uow,
        IWhatsAppQueue whatsAppQueue,
        ISchoolNotificationService notify,
        IGeocodingService geocoding,
        IFcmSender fcm,
        IConfiguration config,
        ILogger<BusAttendanceController> logger)
    {
        _uow = uow;
        _whatsAppQueue = whatsAppQueue;
        _notify = notify;
        _geocoding = geocoding;
        _fcm = fcm;
        _config = config;
        _logger = logger;
    }

    [HttpPost("pick-in")]
    [Authorize(Roles = AppRoles.EventPosters)]
    public Task<ActionResult<ApiResponse<BusEventResponse>>> PickIn(BusEventRequest request, CancellationToken ct)
        => HandleEventAsync(request, BusAttendanceType.PickIn, ct);

    [HttpPost("drop-out")]
    [Authorize(Roles = AppRoles.EventPosters)]
    public Task<ActionResult<ApiResponse<BusEventResponse>>> DropOut(BusEventRequest request, CancellationToken ct)
        => HandleEventAsync(request, BusAttendanceType.DropOut, ct);

    private async Task<ActionResult<ApiResponse<BusEventResponse>>> HandleEventAsync(
        BusEventRequest request, BusAttendanceType type, CancellationToken ct)
    {
        var student = await _uow.Students.Query(false)
            .Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == request.StudentId && s.IsActive, ct);
        if (student is null)
            return NotFoundEnvelope($"Student {request.StudentId} was not found or is inactive.");

        // Resolve the driver: drivers act as themselves; staff must say which driver.
        int driverId;
        if (IsDriver)
        {
            var ownId = await _uow.Drivers.Query()
                .Where(d => d.UserId == CallerUserId)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(ct);
            if (ownId is null)
                return BadRequestEnvelope("This account has no driver profile linked to it.");
            driverId = ownId.Value;
        }
        else
        {
            if (request.DriverId is null)
                return BadRequestEnvelope("DriverId is required when the caller is not a driver.");
            driverId = request.DriverId.Value;
        }

        var driver = await _uow.Drivers.FindAsync(new object[] { driverId }, ct);
        if (driver is null)
            return NotFoundEnvelope($"Driver {driverId} was not found.");

        var busId = request.BusId ?? student.BusId;
        if (busId is null)
            return BadRequestEnvelope("Bus could not be determined - the student is not assigned to a bus.");
        var bus = await _uow.Buses.FindAsync(new object[] { busId.Value }, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {busId} was not found.");

        if (request.TripId is { } tripId)
        {
            var tripOk = await _uow.Trips.AnyAsync(
                t => t.Id == tripId && t.BusId == bus.Id && t.Status == TripStatus.Active, ct);
            if (!tripOk)
                return BadRequestEnvelope($"Trip {tripId} is not an active trip of Bus #{bus.BusNumber}.");
        }

        // Offline-sync friendly: the driver device's timestamp is the truth; clamp bad clocks.
        var timestampUtc = request.TimestampUtc is { } ts
            ? (ts.Kind == DateTimeKind.Utc ? ts : DateTime.SpecifyKind(ts, DateTimeKind.Utc))
            : DateTime.UtcNow;
        if (timestampUtc > DateTime.UtcNow.AddMinutes(5))
            timestampUtc = DateTime.UtcNow;

        // 30-second debounce: an accidental double-tap (or offline-sync re-post) of the SAME
        // event type for the SAME student inside the window returns the original event —
        // no duplicate attendance row, no second WhatsApp. Idempotent by design.
        var debounceSeconds = _config.GetValue("BusAttendance:DebounceSeconds", 30);
        var windowStart = timestampUtc.AddSeconds(-debounceSeconds);
        var windowEnd = timestampUtc.AddSeconds(debounceSeconds);
        var duplicate = await _uow.BusAttendances.Query(false)
            .Where(e => e.StudentId == student.Id && e.Type == type
                        && e.TimestampUtc >= windowStart && e.TimestampUtc <= windowEnd)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefaultAsync(ct);
        if (duplicate is not null)
        {
            _logger.LogInformation(
                "Debounced duplicate {Type} for student {StudentId} (original event {EventId}).",
                type, student.Id, duplicate.Id);
            return OkEnvelope(new BusEventResponse(
                duplicate.Id,
                type.ToString(),
                PkTime.ToPk(duplicate.TimestampUtc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture),
                duplicate.LocationLabel ?? string.Empty,
                AdminNotified: true,
                WhatsAppRecipientsQueued: 0),
                $"{student.FullName}: {type} already recorded seconds ago - duplicate tap ignored.");
        }

        var location = await _geocoding.GetLocationLabelAsync(request.Latitude, request.Longitude, ct);

        var busEvent = new BusAttendance
        {
            StudentId = student.Id,
            BusId = bus.Id,
            DriverId = driver.Id,
            TripId = request.TripId,
            Type = type,
            TimestampUtc = timestampUtc,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            LocationLabel = location,
            WhatsAppSent = false
        };
        await _uow.BusAttendances.AddAsync(busEvent, ct);
        await _uow.SaveChangesAsync(ct);

        var timePk = PkTime.ToPk(timestampUtc);
        var stampText = timePk.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture);

        // Official EN / Roman-Urdu templates. Per-parent language preference overrides
        // the school-wide default (Twilio:Language) — the parent app setting toggles it.
        var defaultLanguage = _config["Twilio:Language"]?.Trim().ToLowerInvariant() ?? "en";
        var templateVars = new WhatsAppTemplates.TemplateVars(
            StudentName: student.FullName,
            ClassName: student.Class.DisplayName,
            BusNumber: bus.BusNumber,
            Time: stampText,
            Location: location);
        var messagesByLang = new Dictionary<string, string>(2);
        string MessageFor(string lang) => messagesByLang.TryGetValue(lang, out var cached)
            ? cached
            : messagesByLang[lang] = type == BusAttendanceType.PickIn
                ? WhatsAppTemplates.PickIn(templateVars, lang)
                : WhatsAppTemplates.DropOut(templateVars, lang);

        // (1) Real-time alert: WPF admin desktop + parent app (their child only).
        try
        {
            var notification = new BusEventNotification
            {
                EventId = busEvent.Id,
                StudentId = student.Id,
                StudentName = student.FullName,
                ClassName = student.Class.DisplayName,
                BusId = bus.Id,
                BusNumber = bus.BusNumber,
                DriverName = driver.FullName,
                EventType = type.ToString(),
                TimePakistan = stampText,
                Latitude = busEvent.Latitude,
                Longitude = busEvent.Longitude,
                LocationLabel = location
            };

            if (type == BusAttendanceType.PickIn)
                await _notify.SendPickInAlertAsync(notification, ct);
            else
                await _notify.SendDropOutAlertAsync(notification, ct);
        }
        catch (Exception ex)
        {
            // A SignalR glitch must never fail a bus event - the DB already has it.
            _logger.LogWarning(ex, "SignalR notification failed for event {EventId}", busEvent.Id);
        }

        // (2) Linked parent accounts drive: FCM push, per-parent language, WhatsApp opt-out.
        var parents = await _uow.ParentStudents.Query()
            .Where(ps => ps.StudentId == student.Id)
            .Join(_uow.Users.Query(), ps => ps.ParentUserId, u => u.Id,
                (ps, u) => new { u.Id, u.Phone, u.PreferredLanguage, u.ReceiveWhatsApp, u.IsActive, u.Role })
            .ToListAsync(ct);

        // (2a) FCM push to active linked parents only (best-effort, grouped by language).
        try
        {
            var pushParents = parents.Where(p => p.IsActive && p.Role == UserRole.Parent).ToArray();
            var parentIds = pushParents.Select(p => p.Id).ToArray();
            if (parentIds.Length == 0)
                _logger.LogWarning("FCM skipped for bus event {EventId}, student {StudentId}: no active parent accounts are linked.",
                    busEvent.Id, student.Id);
            else
            {
                var devices = await _uow.DeviceTokens.Query()
                    .Where(d => parentIds.Contains(d.UserId))
                    .Select(d => new { d.UserId, d.Token })
                    .ToListAsync(ct);
                var parentsWithoutDevices = parentIds.Except(devices.Select(d => d.UserId)).Count();
                if (parentsWithoutDevices > 0)
                    _logger.LogWarning("FCM for bus event {EventId}, student {StudentId}: {Count} linked parent account(s) have no registered devices.",
                        busEvent.Id, student.Id, parentsWithoutDevices);

                foreach (var grp in devices.GroupBy(d =>
                             pushParents.First(p => p.Id == d.UserId).PreferredLanguage == "ur" ? "ur" : "en"))
                {
                    var picked = type == BusAttendanceType.PickIn;
                    var (title, body) = grp.Key == "ur"
                        ? ($"🚌 {(picked ? "Pick-In" : "Drop-Out")}: {student.FullName}",
                           picked ? $"{student.FullName} Bus #{bus.BusNumber} mai baith chuka hai — waqt: {stampText}"
                                  : $"{student.FullName} Bus #{bus.BusNumber} se mehfooz utar chuka hai — waqt: {stampText}")
                        : ($"🚌 {(picked ? "Pick-In" : "Drop-Out")}: {student.FullName}",
                           picked ? $"{student.FullName} has BOARDED Bus #{bus.BusNumber} at {stampText}."
                                  : $"{student.FullName} has been DROPPED OFF safely at {stampText}.");

                    await _fcm.SendToDevicesAsync(grp.Select(d => d.Token).ToArray(),
                        new FcmPush(title, body, new Dictionary<string, string>
                        {
                            ["type"] = "bus_event",
                            ["studentId"] = student.Id.ToString(),
                            ["eventType"] = type.ToString(),
                            ["eventId"] = busEvent.Id.ToString()
                        }), ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM push failed for event {EventId}", busEvent.Id);
        }

        // (2b) WhatsApp to parents - durable log row first, then enqueue.
        var recipients = new List<string> { student.FatherPhone };
        if (_config.GetValue("Twilio:NotifyMother", false) && !string.IsNullOrWhiteSpace(student.MotherPhone))
            recipients.Add(student.MotherPhone!);

        var queued = 0;
        foreach (var phone in recipients)
        {
            // Parent-app toggle: that parent disabled WhatsApp alerts → respect it.
            var owner = parents.FirstOrDefault(p => PhoneMatches(p.Phone, phone));
            if (owner is { ReceiveWhatsApp: false })
            {
                _logger.LogInformation("WhatsApp skipped for {Phone} — parent disabled alerts in app settings.", phone);
                continue;
            }

            var message = MessageFor(owner?.PreferredLanguage == "ur" ? "ur" : defaultLanguage);
            var correlationId = Guid.NewGuid();
            await _uow.WhatsAppLogs.AddAsync(new WhatsAppLog
            {
                CorrelationId = correlationId,
                StudentId = student.Id,
                EventType = type.ToString(), // "PickIn" | "DropOut" — used by rate-limit/debounce views
                ParentPhone = phone,
                Message = message,
                Status = WhatsAppStatus.Queued,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);
            await _uow.SaveChangesAsync(ct);

            await _whatsAppQueue.EnqueueAsync(
                new WhatsAppJob(correlationId, busEvent.Id, student.Id, phone, message), ct);
            queued++;
        }

        return OkEnvelope(new BusEventResponse(
            busEvent.Id,
            type.ToString(),
            stampText,
            location,
            AdminNotified: true,
            WhatsAppRecipientsQueued: queued),
            type == BusAttendanceType.PickIn
                ? $"{student.FullName} marked as boarded."
                : $"{student.FullName} marked as dropped.");
    }

    /// <summary>Loose phone comparison: last 10 digits (covers 0300… vs +92300… formats).</summary>
    private static bool PhoneMatches(string? userPhone, string recipientPhone)
    {
        if (string.IsNullOrWhiteSpace(userPhone)) return false;
        var a = new string(userPhone.Where(char.IsDigit).ToArray());
        var b = new string(recipientPhone.Where(char.IsDigit).ToArray());
        return a.Length >= 10 && b.Length >= 10
               && a[^10..] == b[^10..];
    }

    /// <summary>Event feed for one student. Staff/teachers, or the student's own parent.</summary>
    [HttpGet("student/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<BusEventListItem>>>> GetByStudent(
        int studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (IsParent)
        {
            var owns = await _uow.ParentStudents.AnyAsync(
                ps => ps.ParentUserId == CallerUserId && ps.StudentId == studentId, ct);
            if (!owns)
                return Forbid();
        }
        else if (!IsStaff && CallerRole != AppRoles.Teacher && !IsDriver)
        {
            return Forbid();
        }

        return await QueryEvents(ct, studentId: studentId, page: page, pageSize: pageSize);
    }

    /// <summary>Event feed for one bus. Staff/teachers, or the bus's own driver.</summary>
    [HttpGet("bus/{busId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<BusEventListItem>>>> GetByBus(
        int busId,
        [FromQuery] string? date,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (IsDriver)
        {
            var ownsBus = await _uow.Buses.Query()
                .AnyAsync(b => b.Id == busId && b.Driver != null && b.Driver.UserId == CallerUserId, ct);
            if (!ownsBus)
                return Forbid();
        }
        else if (!IsStaff && CallerRole != AppRoles.Teacher)
        {
            return Forbid();
        }

        return await QueryEvents(ct, busId: busId, date: date, page: page, pageSize: pageSize);
    }

    /// <summary>Everything that happened today (PST), optionally one bus. The admin live feed.</summary>
    [HttpGet("today")]
    [Authorize(Roles = AppRoles.StaffAndTeachers + "," + AppRoles.Driver)]
    public async Task<ActionResult<ApiResponse<PagedResult<BusEventListItem>>>> GetToday(
        [FromQuery] int? busId,
        [FromQuery] BusAttendanceType? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (IsDriver)
        {
            var ownBusId = await _uow.Buses.Query()
                .Where(b => b.Driver != null && b.Driver.UserId == CallerUserId)
                .Select(b => (int?)b.Id)
                .FirstOrDefaultAsync(ct);
            if (ownBusId is null)
                return OkEnvelope(PagedResult<BusEventListItem>.Create(
                    new List<BusEventListItem>(), page, pageSize, 0));
            busId = ownBusId;
        }

        var today = PkTime.TodayPk;
        return await QueryEvents(ct, busId: busId, type: type, date: today.ToString("yyyy-MM-dd"),
            page: page, pageSize: pageSize);
    }

    private async Task<ActionResult<ApiResponse<PagedResult<BusEventListItem>>>> QueryEvents(
        CancellationToken ct,
        int? studentId = null,
        int? busId = null,
        BusAttendanceType? type = null,
        string? date = null,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.BusAttendances.Query().AsQueryable();
        if (studentId is not null) query = query.Where(e => e.StudentId == studentId);
        if (busId is not null) query = query.Where(e => e.BusId == busId);
        if (type is not null) query = query.Where(e => e.Type == type);
        if (!string.IsNullOrWhiteSpace(date)
            && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pkDate))
        {
            var startUtc = PkTime.PkDayStartUtc(pkDate);
            var endUtc = PkTime.PkDayEndUtc(pkDate);
            query = query.Where(e => e.TimestampUtc >= startUtc && e.TimestampUtc < endUtc);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.TimestampUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new
            {
                e.Id, e.StudentId, e.BusId, e.Type, e.TimestampUtc,
                e.Latitude, e.Longitude, e.LocationLabel, e.WhatsAppSent,
                StudentName = e.Student.FullName,
                ClassName = e.Student.Class.Name + "-" + e.Student.Class.Section,
                BusNumber = e.Bus.BusNumber,
                DriverName = e.Driver.FullName
            })
            .ToListAsync(ct);

        var items = rows.Select(e => new BusEventListItem(
            e.Id, e.StudentId, e.StudentName, e.ClassName, e.BusId, e.BusNumber, e.DriverName,
            e.Type.ToString(),
            PkTime.ToPk(e.TimestampUtc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture),
            e.Latitude, e.Longitude, e.LocationLabel, e.WhatsAppSent)).ToList();

        return OkEnvelope(PagedResult<BusEventListItem>.Create(items, page, pageSize, total));
    }
}
