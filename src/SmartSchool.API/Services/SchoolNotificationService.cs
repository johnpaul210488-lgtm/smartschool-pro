using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SmartSchool.API.Hubs;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Services;

/// <summary>
/// Server-side SignalR fan-out used by controllers. Targets the AdminGroup
/// (every connected WPF/staff client) and ParentGroup_{studentId} so the
/// parent app can also receive instant alerts for its own children.
/// </summary>
public sealed class SchoolNotificationService : ISchoolNotificationService
{
    private readonly IHubContext<SchoolNotificationHub> _hub;
    private readonly IUnitOfWork _uow;
    private readonly IFcmSender _fcm;
    private readonly IWhatsAppMessageService _whatsapp;
    private readonly ILogger<SchoolNotificationService> _logger;

    public SchoolNotificationService(IHubContext<SchoolNotificationHub> hub,
                                     IUnitOfWork uow,
                                     IFcmSender fcm,
                                     IWhatsAppMessageService whatsapp,
                                     ILogger<SchoolNotificationService> logger)
    {
        _hub = hub;
        _uow = uow;
        _fcm = fcm;
        _whatsapp = whatsapp;
        _logger = logger;
    }

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

    /// <summary>Generic red-flag toast for every connected admin/staff client (e.g. WhatsApp permanently failed).</summary>
    public Task SendAdminAlertAsync(AdminAlertNotification a, CancellationToken ct = default)
        => _hub.Clients.Group(HubGroups.AdminGroup)
              .SendAsync(HubEvents.SendAdminAlert, a, ct);

    public Task SendAnnouncementAsync(AnnouncementNotification a, CancellationToken ct = default)
        => _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.ParentsGroup)
              .SendAsync(HubEvents.ReceiveAnnouncement, a, ct);

    /// <summary>Fee due / overdue alert → linked parents (SignalR + FCM + WhatsApp) + admin desktops.</summary>
    public async Task SendFeeDueAlertAsync(FeeDueNotification n, CancellationToken ct = default)
    {
        // (1) SignalR → admin desktops + the student's parent group
        await _hub.Clients.Groups(HubGroups.AdminGroup, HubGroups.ParentGroup(n.StudentId))
                      .SendAsync(HubEvents.SendFeeDueAlert, n, ct);

        // (2) FCM push to active linked parents of this student
        try
        {
            var parents = await _uow.ParentStudents.Query()
                .Where(ps => ps.StudentId == n.StudentId)
                .Join(_uow.Users.Query(), ps => ps.ParentUserId, u => u.Id,
                    (ps, u) => new { u.Id, u.PreferredLanguage, u.IsActive, u.Role, u.Phone })
                .ToListAsync(ct);

            var pushParents = parents.Where(p => p.IsActive && p.Role == UserRole.Parent).ToArray();
            var parentIds = pushParents.Select(p => p.Id).ToArray();
            if (parentIds.Length == 0) return;

            var devices = await _uow.DeviceTokens.Query()
                .Where(d => parentIds.Contains(d.UserId))
                .Select(d => new { d.UserId, d.Token })
                .ToListAsync(ct);

            if (devices.Count > 0)
            {
                foreach (var grp in devices.GroupBy(d =>
                             pushParents.First(p => p.Id == d.UserId).PreferredLanguage == "ur" ? "ur" : "en"))
                {
                    var (title, body) = grp.Key == "ur"
                        ? ($"💰 Fee Baki: {n.StudentName}",
                           $"{n.StudentName} ({n.ClassName}) ki {n.Month} ki fee Rs {n.Balance:N2} baki hai. Due: {n.DueDate:dd MMM yyyy}.")
                        : ($"💰 Fee Due: {n.StudentName}",
                           $"{n.StudentName} ({n.ClassName}) — {n.Month} fee balance Rs {n.Balance:N2}. Due: {n.DueDate:dd MMM yyyy}.");

                    await _fcm.SendToDevicesAsync(
                        grp.Select(d => d.Token).ToArray(),
                        new FcmPush(title, body, new Dictionary<string, string>
                        {
                            ["type"] = "fee_due",
                            ["studentId"] = n.StudentId.ToString(),
                            ["amount"] = n.Amount.ToString(),
                            ["balance"] = n.Balance.ToString(),
                            ["month"] = n.Month,
                        }), ct);
                }
            }

            // (3) WhatsApp to active linked parents who have a phone number and have not opted out
            var whatsAppEnabled = await _whatsapp.IsEnabled();
            if (whatsAppEnabled)
            {
                foreach (var p in pushParents)
                {
                    if (string.IsNullOrWhiteSpace(p.Phone)) continue;
                    var phone = PhoneNormalizer.ToE164(p.Phone);
                    if (phone is null) continue;

                    var body = p.PreferredLanguage == "ur"
                        ? $"Assalam o alaikum! {n.StudentName} ({n.ClassName}) ki {n.Month} ki fee Rs {n.Balance:N2} baki hai. Due: {n.DueDate:dd MMM yyyy}. Barah-e-karm ada farmayein."
                        : $"Hello! {n.StudentName} ({n.ClassName}) — {n.Month} fee balance Rs {n.Balance:N2}. Due: {n.DueDate:dd MMM yyyy}. Please pay soon.";

                    await _whatsapp.SendText(phone, body, priority: 10, ct);
                }
            }
        }
        catch (Exception ex)
        {
            // FCM/WhatsApp glitch must never fail the fee flow — SignalR already delivered.
            _logger.LogWarning(ex, "Fee-due push notifications failed for student {StudentId}", n.StudentId);
        }
    }
}
