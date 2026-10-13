using Microsoft.AspNetCore.SignalR;
using SmartSchool.API.Hubs;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;

namespace SmartSchool.API.Services;

/// <summary>
/// Server-side SignalR fan-out used by controllers. Targets the AdminGroup
/// (every connected WPF/staff client) and ParentGroup_{studentId} so the
/// parent app can also receive instant alerts for its own children.
/// </summary>
public sealed class SchoolNotificationService : ISchoolNotificationService
{
    private readonly IHubContext<SchoolNotificationHub> _hub;

    public SchoolNotificationService(IHubContext<SchoolNotificationHub> hub) => _hub = hub;

    public Task SendPickInAlertAsync(BusEventNotification n, CancellationToken ct = default)
        => _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.ParentGroup(n.StudentId))
              .SendAsync(HubEvents.SendPickInAlert, n, ct);

    public Task SendDropOutAlertAsync(BusEventNotification n, CancellationToken ct = default)
        => _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.ParentGroup(n.StudentId))
              .SendAsync(HubEvents.SendDropOutAlert, n, ct);

    public Task SendTripUpdateAsync(TripNotification n, CancellationToken ct = default)
        => _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.BusGroup(n.BusId))
              .SendAsync(HubEvents.SendTripUpdate, n, ct);

    public Task SendBusLocationAsync(BusLocationUpdate u, CancellationToken ct = default)
        => _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.BusGroup(u.BusId))
              .SendAsync(HubEvents.UpdateBusLocation, u, ct);
}
