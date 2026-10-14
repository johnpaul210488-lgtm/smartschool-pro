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

[ApiController]
[Route("api/[controller]")]
public sealed class BusesController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public BusesController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    [Authorize(Roles = AppRoles.StaffAndTeachers + "," + AppRoles.Driver)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusListItem>>>> GetAll(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _uow.Buses.Query().AsQueryable();
        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        var items = await query.OrderBy(b => b.BusNumber)
            .Select(b => new BusListItem(
                b.Id, b.BusNumber, b.Route, b.VehicleNo, b.Capacity,
                b.DriverId, b.Driver != null ? b.Driver.FullName : null,
                b.IsActive, b.Students.Count(s => s.IsActive)))
            .ToListAsync(ct);

        return OkEnvelope(items);
    }

    /// <summary>Driver app: caller ka apna assigned bus (ek call - no id guessing).</summary>
    [HttpGet("mine")]
    [Authorize(Roles = AppRoles.Driver)]
    public async Task<ActionResult<ApiResponse<BusListItem>>> Mine(CancellationToken ct)
    {
        var bus = await _uow.Buses.Query().Include(b => b.Driver)
            .Where(b => b.Driver != null && b.Driver.UserId == CallerUserId && b.IsActive)
            .Select(b => new BusListItem(
                b.Id, b.BusNumber, b.Route, b.VehicleNo, b.Capacity,
                b.DriverId, b.Driver!.FullName, b.IsActive, b.Students.Count(s => s.IsActive)))
            .FirstOrDefaultAsync(ct);

        return bus is null
            ? NotFoundEnvelope("Is account se koi bus assign nahi hai. Admin se rabta karein.")
            : OkEnvelope(bus);
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<BusListItem>>> Create(BusUpsertRequest request, CancellationToken ct)
    {
        var number = request.BusNumber.Trim();
        if (await _uow.Buses.AnyAsync(b => b.BusNumber == number, ct))
            return ConflictEnvelope($"Bus number '{number}' already exists.");

        var bus = new Bus
        {
            BusNumber = number,
            Route = request.Route.Trim(),
            VehicleNo = request.VehicleNo.Trim(),
            Capacity = request.Capacity,
            IsActive = request.IsActive
        };
        await _uow.Buses.AddAsync(bus, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedEnvelope(new BusListItem(
            bus.Id, bus.BusNumber, bus.Route, bus.VehicleNo, bus.Capacity, null, null, bus.IsActive, 0),
            "Bus created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, BusUpsertRequest request, CancellationToken ct)
    {
        var bus = await _uow.Buses.FindAsync(new object[] { id }, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {id} was not found.");

        var number = request.BusNumber.Trim();
        if (await _uow.Buses.AnyAsync(b => b.Id != id && b.BusNumber == number, ct))
            return ConflictEnvelope($"Bus number '{number}' already exists.");

        bus.BusNumber = number;
        bus.Route = request.Route.Trim();
        bus.VehicleNo = request.VehicleNo.Trim();
        bus.Capacity = request.Capacity;
        bus.IsActive = request.IsActive;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Bus updated successfully.");
    }

    /// <summary>Assign (or with null, unassign) the driver. A driver drives exactly one bus at a time.</summary>
    [HttpPut("{id:int}/assign-driver")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> AssignDriver(int id, AssignDriverRequest request, CancellationToken ct)
    {
        var bus = await _uow.Buses.FindAsync(new object[] { id }, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {id} was not found.");

        if (request.DriverId is null)
        {
            bus.DriverId = null;
            await _uow.SaveChangesAsync(ct);
            return OkMessage("Driver unassigned from bus.");
        }

        var driver = await _uow.Drivers.FindAsync(new object[] { request.DriverId.Value }, ct);
        if (driver is null || !driver.IsActive)
            return BadRequestEnvelope($"Driver {request.DriverId} does not exist or is inactive.");

        var otherBus = await _uow.Buses.Query()
            .FirstOrDefaultAsync(b => b.DriverId == driver.Id && b.Id != id, ct);
        if (otherBus is not null)
            return ConflictEnvelope($"Driver is already assigned to Bus #{otherBus.BusNumber}. Unassign first.");

        bus.DriverId = driver.Id;
        await _uow.SaveChangesAsync(ct);
        return OkMessage($"Driver assigned to Bus #{bus.BusNumber}.");
    }

    /// <summary>Students-on-this-bus list for the DRIVER app. Drivers may only fetch their own bus.</summary>
    [HttpGet("{id:int}/manifest")]
    [Authorize(Roles = AppRoles.Driver + "," + AppRoles.Admin + "," + AppRoles.SuperAdmin + "," + AppRoles.Teacher)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusManifestItem>>>> Manifest(int id, CancellationToken ct)
    {
        var bus = await _uow.Buses.Query().Include(b => b.Driver)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {id} was not found.");

        if (IsDriver && (bus.Driver is null || bus.Driver.UserId != CallerUserId))
            return Forbid();

        var todayStartUtc = PkTime.PkDayStartUtc(PkTime.TodayPk);
        var todayEndUtc = PkTime.PkDayEndUtc(PkTime.TodayPk);

        var todaysEvents = await _uow.BusAttendances.Query()
            .Where(e => e.BusId == id && e.TimestampUtc >= todayStartUtc && e.TimestampUtc < todayEndUtc)
            .Select(e => new { e.StudentId, e.Type, e.TimestampUtc })
            .ToListAsync(ct);

        var lastStatusByStudent = todaysEvents
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TimestampUtc).First().Type.ToString());

        var students = await _uow.Students.Query()
            .Where(s => s.BusId == id && s.IsActive)
            .OrderBy(s => s.FullName)
            .Select(s => new
            {
                s.Id, s.FullName, s.RollNo, s.PhotoUrl, s.FatherName, s.FatherPhone, s.MotherPhone, s.Address,
                ClassName = s.Class.Name + "-" + s.Class.Section
            })
            .ToListAsync(ct);

        var manifest = students.Select(s => new BusManifestItem(
            s.Id, s.FullName, s.ClassName, s.RollNo, s.PhotoUrl,
            s.FatherName, s.FatherPhone, s.MotherPhone, s.Address,
            lastStatusByStudent.TryGetValue(s.Id, out var status) ? status : null)).ToList();

        return OkEnvelope<IReadOnlyList<BusManifestItem>>(manifest);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var bus = await _uow.Buses.FindAsync(new object[] { id }, ct);
        if (bus is null)
            return NotFoundEnvelope($"Bus {id} was not found.");
        if (await _uow.Students.AnyAsync(s => s.BusId == id, ct))
            return ConflictEnvelope("Students are still assigned to this bus. Reassign them first.");
        if (await _uow.Trips.AnyAsync(t => t.BusId == id, ct)
            || await _uow.BusAttendances.AnyAsync(a => a.BusId == id, ct))
            return ConflictEnvelope("This bus has trip/attendance history - deactivate it instead of deleting.");

        _uow.Buses.Remove(bus);
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Bus deleted successfully.");
    }
}
