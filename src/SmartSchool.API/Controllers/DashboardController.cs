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
[Route("api/[controller]")]
[Authorize(Roles = AppRoles.StaffAndTeachers)]
public sealed class DashboardController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public DashboardController(IUnitOfWork uow) => _uow = uow;

    /// <summary>One round-trip with every number the WPF admin dashboard home screen needs.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummary>>> Summary(CancellationToken ct)
    {
        var today = PkTime.TodayPk;
        var dayStartUtc = PkTime.PkDayStartUtc(today);
        var dayEndUtc = PkTime.PkDayEndUtc(today);
        var monthPrefix = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        // Awaited sequentially - one DbContext cannot run queries in parallel.
        var activeStudents = await _uow.Students.CountAsync(s => s.IsActive, ct);
        var totalTeachers = await _uow.Teachers.CountAsync(t => t.IsActive, ct);
        var totalBuses = await _uow.Buses.CountAsync(b => b.IsActive, ct);
        var totalDrivers = await _uow.Drivers.CountAsync(d => d.IsActive, ct);
        var activeTrips = await _uow.Trips.CountAsync(t => t.Status == TripStatus.Active, ct);
        var pickIns = await _uow.BusAttendances.CountAsync(
            e => e.Type == BusAttendanceType.PickIn && e.TimestampUtc >= dayStartUtc && e.TimestampUtc < dayEndUtc, ct);
        var dropOuts = await _uow.BusAttendances.CountAsync(
            e => e.Type == BusAttendanceType.DropOut && e.TimestampUtc >= dayStartUtc && e.TimestampUtc < dayEndUtc, ct);
        var pendingCount = await _uow.Fees.CountAsync(f => f.Status != FeeStatus.Paid, ct);
        var pendingAmount = await _uow.Fees.Query()
            .Where(f => f.Status != FeeStatus.Paid)
            .SumAsync(f => (decimal?)f.Amount, ct) ?? 0m;
        var collected = await _uow.Fees.Query()
            .Where(f => f.Status == FeeStatus.Paid && f.Month == monthPrefix)
            .SumAsync(f => (decimal?)f.Amount, ct) ?? 0m;
        var waSentToday = await _uow.WhatsAppLogs.CountAsync(
            l => l.Status == WhatsAppStatus.Sent && l.SentAtUtc >= dayStartUtc && l.SentAtUtc < dayEndUtc, ct);
        var waFailed = await _uow.WhatsAppLogs.CountAsync(l => l.Status == WhatsAppStatus.Failed, ct);

        return OkEnvelope(new DashboardSummary(
            PkTime.NowPk.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture),
            activeStudents, totalTeachers, totalBuses, totalDrivers,
            activeTrips, pickIns, dropOuts,
            pendingCount, pendingAmount, collected,
            waSentToday, waFailed));
    }
}
