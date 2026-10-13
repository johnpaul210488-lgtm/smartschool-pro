using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Hubs;

/// <summary>
/// SignalR hub at /hubs/notifications (JWT via ?access_token=...).
///
/// Groups (spec): "AdminGroup", "ParentGroup_{studentId}" (plus BusGroup_{busId}
/// for the live-map screen).
///
/// Server -> client events (spec method names): SendPickInAlert, SendDropOutAlert,
/// UpdateBusLocation (plus SendTripUpdate).
///
/// Client -> server methods: WatchBus / UnwatchBus (staff), UpdateBusLocation (driver
/// streams the bus GPS for the live map; pick-in/drop-out themselves go through the
/// REST endpoint so they are persisted before broadcast).
/// </summary>
[Authorize]
public sealed class SchoolNotificationHub : Hub
{
    private readonly SchoolDbContext _db;

    public SchoolNotificationHub(SchoolDbContext db) => _db = db;

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (role is AppRoles.SuperAdmin or AppRoles.Admin or AppRoles.Teacher)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.AdminGroup);
        }
        else if (role == AppRoles.Parent
                 && int.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parentId))
        {
            // Parents join one ParentGroup_{studentId} per linked child.
            var childIds = await _db.ParentStudents.AsNoTracking()
                .Where(ps => ps.ParentUserId == parentId)
                .Select(ps => ps.StudentId)
                .ToListAsync();

            foreach (var studentId in childIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.ParentGroup(studentId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Staff subscribes to one specific bus (live map screen).</summary>
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public Task WatchBus(int busId)
        => Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.BusGroup(busId));

    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public Task UnwatchBus(int busId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.BusGroup(busId));

    /// <summary>
    /// Driver app streams bus GPS during an active trip; the server relays it to the
    /// admin group + live-map watchers as an UpdateBusLocation broadcast.
    /// </summary>
    [Authorize(Roles = AppRoles.Driver)]
    public async Task UpdateBusLocation(int busId, double latitude, double longitude, double? speedKmh)
    {
        var update = new BusLocationUpdate
        {
            BusId = busId,
            BusNumber = await _db.Buses.AsNoTracking()
                .Where(b => b.Id == busId)
                .Select(b => b.BusNumber)
                .FirstOrDefaultAsync() ?? "?",
            DriverName = Context.User?.FindFirst("fullName")?.Value
                          ?? Context.User?.FindFirst(ClaimTypes.Name)?.Value
                          ?? "Driver",
            Latitude = latitude,
            Longitude = longitude,
            SpeedKmh = speedKmh,
            TimePakistan = PkTime.NowPk.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture)
        };

        await Clients.Groups(HubGroups.AdminGroup, HubGroups.BusGroup(busId))
            .SendAsync(HubEvents.UpdateBusLocation, update);
    }
}
