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

/// <summary>Trip lifecycle: driver starts a trip on his bus, ends it after the run.</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class TripsController : BaseApiController
{
    private readonly IUnitOfWork _uow;
    private readonly ISchoolNotificationService _notify;
    private readonly ILogger<TripsController> _logger;
    private readonly SmartSchool.API.Services.LiveBusTracker _live;

    public TripsController(IUnitOfWork uow, ISchoolNotificationService notify, ILogger<TripsController> logger,
        SmartSchool.API.Services.LiveBusTracker live)
    {
        _uow = uow;
        _notify = notify;
        _logger = logger;
        _live = live;
    }

    private static string Fmt(DateTime utc)
        => PkTime.ToPk(utc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture);

    private async Task<int?> ResolveCallerDriverIdAsync(CancellationToken ct)
    {
        if (!IsDriver)
            return null;
        return await _uow.Drivers.Query()
            .Where(d => d.UserId == CallerUserId)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);
    }

    [HttpPost("start")]
    [Authorize(Roles = AppRoles.EventPosters)]
    public async Task<ActionResult<ApiResponse<TripResponse>>> Start(TripStartRequest request, CancellationToken ct)
    {
        var bus = await _uow.Buses.Query(false).Include(b => b.Driver)
            .FirstOrDefaultAsync(b => b.Id == request.BusId && b.IsActive, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {request.BusId} was not found or is inactive.");

        var callerDriverId = await ResolveCallerDriverIdAsync(ct);

        int driverId;
        if (IsDriver)
        {
            if (callerDriverId is null)
                return BadRequestEnvelope("This account has no driver profile linked to it.");
            if (bus.DriverId != callerDriverId)
                return ConflictEnvelope($"Bus #{bus.BusNumber} is not assigned to you.");
            driverId = callerDriverId.Value;
        }
        else
        {
            if (bus.DriverId is null)
                return BadRequestEnvelope($"Bus #{bus.BusNumber} has no assigned driver.");
            driverId = bus.DriverId.Value;
        }

        var existing = await _uow.Trips.Query()
            .FirstOrDefaultAsync(t => t.BusId == bus.Id && t.Status == TripStatus.Active, ct);
        if (existing is not null)
            return ConflictEnvelope($"Bus #{bus.BusNumber} already has an active trip (id {existing.Id}). End it first.");

        var trip = new Trip
        {
            BusId = bus.Id,
            DriverId = driverId,
            Type = request.Type,
            Status = TripStatus.Active,
            StartedAtUtc = DateTime.UtcNow
        };
        await _uow.Trips.AddAsync(trip, ct);
        await _uow.SaveChangesAsync(ct);

        await NotifySafe(new TripNotification
        {
            TripId = trip.Id,
            BusId = bus.Id,
            BusNumber = bus.BusNumber,
            DriverName = bus.Driver?.FullName ?? string.Empty,
            Type = trip.Type.ToString(),
            Status = trip.Status.ToString(),
            TimePakistan = Fmt(trip.StartedAtUtc)
        }, ct);

        return CreatedEnvelope(new TripResponse(
            trip.Id, bus.Id, bus.BusNumber, bus.Route, driverId,
            bus.Driver?.FullName ?? string.Empty,
            trip.Type.ToString(), trip.Status.ToString(),
            Fmt(trip.StartedAtUtc), null, 0),
            $"Trip started on Bus #{bus.BusNumber}.");
    }

    [HttpPost("{id:int}/end")]
    [Authorize(Roles = AppRoles.EventPosters)]
    public async Task<ActionResult<ApiResponse<TripResponse>>> End(int id, CancellationToken ct)
    {
        var trip = await _uow.Trips.Query(false)
            .Include(t => t.Bus)
            .Include(t => t.Driver)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trip is null)
            return NotFoundEnvelope($"Trip {id} was not found.");
        if (trip.Status != TripStatus.Active)
            return ConflictEnvelope($"Trip {id} is already {trip.Status}.");

        var callerDriverId = await ResolveCallerDriverIdAsync(ct);
        if (IsDriver)
        {
            if (callerDriverId is null)
                return BadRequestEnvelope("This account has no driver profile linked to it.");
            if (callerDriverId != trip.DriverId)
                return ConflictEnvelope("Only the trip's own driver (or staff) can end it.");
        }

        trip.Status = TripStatus.Completed;
        _live.Remove(trip.BusId);
        trip.EndedAtUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        await NotifySafe(new TripNotification
        {
            TripId = trip.Id,
            BusId = trip.BusId,
            BusNumber = trip.Bus.BusNumber,
            DriverName = trip.Driver.FullName,
            Type = trip.Type.ToString(),
            Status = trip.Status.ToString(),
            TimePakistan = Fmt(trip.EndedAtUtc.Value)
        }, ct);

        var eventCount = await _uow.BusAttendances.CountAsync(e => e.TripId == trip.Id, ct);

        return OkEnvelope(new TripResponse(
            trip.Id, trip.BusId, trip.Bus.BusNumber, trip.Bus.Route, trip.DriverId, trip.Driver.FullName,
            trip.Type.ToString(), trip.Status.ToString(),
            Fmt(trip.StartedAtUtc), Fmt(trip.EndedAtUtc.Value), eventCount),
            $"Trip ended. {eventCount} bus events recorded.");
    }

    /// <summary>Active trips right now (staff see all; the driver sees his own).</summary>
    [HttpGet("active")]
    [Authorize(Roles = AppRoles.StaffAndTeachers + "," + AppRoles.Driver)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TripResponse>>>> Active(CancellationToken ct)
    {
        var query = _uow.Trips.Query()
            .Include(t => t.Bus).Include(t => t.Driver)
            .Where(t => t.Status == TripStatus.Active);

        if (IsDriver)
            query = query.Where(t => t.Driver.UserId == CallerUserId);

        var trips = await query.OrderByDescending(t => t.StartedAtUtc).ToListAsync(ct);

        return OkEnvelope<IReadOnlyList<TripResponse>>(trips.Select(t => new TripResponse(
            t.Id, t.BusId, t.Bus.BusNumber, t.Bus.Route, t.DriverId, t.Driver.FullName,
            t.Type.ToString(), t.Status.ToString(),
            Fmt(t.StartedAtUtc), null, t.Events.Count)).ToList());
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<PagedResult<TripResponse>>>> GetAll(
        [FromQuery] int? busId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.Trips.Query()
            .Include(t => t.Bus).Include(t => t.Driver).Include(t => t.Events)
            .AsQueryable();
        if (busId is not null)
            query = query.Where(t => t.BusId == busId);

        var total = await query.CountAsync(ct);
        var trips = await query.OrderByDescending(t => t.StartedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = trips.Select(t => new TripResponse(
            t.Id, t.BusId, t.Bus.BusNumber, t.Bus.Route, t.DriverId, t.Driver.FullName,
            t.Type.ToString(), t.Status.ToString(),
            Fmt(t.StartedAtUtc), t.EndedAtUtc is { } end ? Fmt(end) : null,
            t.Events.Count)).ToList();

        return OkEnvelope(PagedResult<TripResponse>.Create(items, page, pageSize, total));
    }

    /// <summary>
    /// Driver app REST uplink for GPS during an active trip. Background Flutter
    /// services can't reliably hold a SignalR socket — REST is the robust path;
    /// server relays it to AdminGroup + watchers via the hub, same as the driver-
    /// called hub method.
    /// </summary>
    [HttpPost("{id:int}/location")]
    [Authorize(Roles = AppRoles.Driver)]
    public async Task<ActionResult<ApiResponse<object>>> PushLocation(
        int id, LocationPingRequest request, CancellationToken ct)
    {
        var trip = await _uow.Trips.Query().Include(t => t.Bus)
            .FirstOrDefaultAsync(t => t.Id == id && t.Status == TripStatus.Active, ct);
        if (trip is null)
            return NotFoundEnvelope($"Active trip {id} nahi mila (shayad end ho chuka).");

        var callerDriverId = await ResolveCallerDriverIdAsync(ct);
        if (callerDriverId is null || trip.DriverId != callerDriverId)
            return BadRequestEnvelope("Yeh trip aapki nahi hai.");

        var update = new SmartSchool.Core.DTOs.BusLocationUpdate
        {
            BusId = trip.BusId,
            BusNumber = trip.Bus.BusNumber,
            DriverName = User.FindFirst("fullName")?.Value ?? User.Identity?.Name ?? "Driver",
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            SpeedKmh = request.SpeedKmh,
            TimePakistan = PkTime.NowPk.ToString("dd MMM yyyy hh:mm:ss tt", CultureInfo.InvariantCulture)
        };
        _live.Update(update);
        await _notify.SendBusLocationAsync(update, ct);

        return OkEnvelope<object>(new { }, "OK");
    }

    /// <summary>Active trip wali buses ki aakhri location: admin/parent map khulte hi yahi load hoti hai.</summary>
    [HttpGet("live")]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SmartSchool.Core.DTOs.BusLocationUpdate>>>> Live(CancellationToken ct)
    {
        var activeBusIds = await _uow.Trips.Query()
            .Where(t => t.Status == TripStatus.Active)
            .Select(t => t.BusId)
            .ToListAsync(ct);
        return OkEnvelope<IReadOnlyList<SmartSchool.Core.DTOs.BusLocationUpdate>>(_live.Snapshot(activeBusIds));
    }

    private async Task NotifySafe(TripNotification notification, CancellationToken ct)
    {
        try
        {
            await _notify.SendTripUpdateAsync(notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR trip notification failed for trip {TripId}", notification.TripId);
        }
    }
}
