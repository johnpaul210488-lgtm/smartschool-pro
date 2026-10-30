using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSchool.API.Controllers;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Infrastructure.Repositories;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using Xunit;

namespace SmartSchool.Tests;

public sealed class NotificationFlowTests
{
    private static SchoolDbContext CreateDb() => new(new DbContextOptionsBuilder<SchoolDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void ParentLinksAreRestrictedToOfficeStaff()
    {
        var authorization = Assert.Single(typeof(ParentLinksController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false));
        Assert.Equal(AppRoles.Staff, authorization.Roles);
    }

    [Fact]
    public async Task ExplicitParentLinkSendsBusEventOnlyToThatParentsDevices()
    {
        await using var db = CreateDb();
        db.Classes.Add(new SchoolClass { Id = 1, Name = "1", Section = "A" });
        db.Users.AddRange(
            new User { Id = 1, Username = "driver", Role = UserRole.Driver },
            new User { Id = 2, Username = "parent", Role = UserRole.Parent, PreferredLanguage = "ur", Phone = "03001234567" },
            // Sharing a phone number never creates a parent-child link.
            new User { Id = 3, Username = "other", Role = UserRole.Parent, Phone = "03001234567" },
            new User { Id = 4, Username = "inactive", Role = UserRole.Parent, IsActive = false });
        db.Drivers.Add(new Driver { Id = 1, UserId = 1, FullName = "Driver" });
        db.Buses.Add(new Bus { Id = 1, BusNumber = "7", DriverId = 1 });
        db.Students.Add(new Student
        {
            Id = 1, ClassId = 1, BusId = 1, FullName = "Student", FatherPhone = "03001234567"
        });
        db.DeviceTokens.AddRange(
            new DeviceToken { UserId = 2, Token = "linked-device", Platform = "android" },
            new DeviceToken { UserId = 3, Token = "unlinked-device", Platform = "android" },
            new DeviceToken { UserId = 4, Token = "inactive-device", Platform = "android" });
        // An account can be deactivated after it was originally linked.
        db.ParentStudents.Add(new ParentStudent { ParentUserId = 4, StudentId = 1 });
        await db.SaveChangesAsync();

        var uow = new UnitOfWork(db);
        var links = new ParentLinksController(uow);
        var request = new LinkParentStudentRequest { ParentUsername = "parent", StudentId = 1 };
        Assert.IsType<OkObjectResult>((await links.Link(request, default)).Result);
        Assert.IsType<OkObjectResult>((await links.Link(request, default)).Result);
        Assert.Single(await db.ParentStudents.Where(p => p.ParentUserId == 2).ToListAsync()); // Idempotent.
        var statusResult = Assert.IsType<OkObjectResult>((await links.ForStudent(1, default)).Result);
        var statusEnvelope = Assert.IsType<ApiResponse<object>>(statusResult.Value);
        var status = Assert.Single(Assert.IsType<List<ParentLinkStatus>>(statusEnvelope.Data), p => p.Username == "parent");
        Assert.Equal(1, status.RegisteredDevices);

        var fcm = new CaptureFcm();
        var queue = new InMemoryWhatsAppQueue(Options.Create(new WhatsAppOptions()));
        var controller = new BusAttendanceController(uow, queue, new NoopNotifications(), new FixedGeocoder(),
            fcm, new ConfigurationBuilder().Build(), NullLogger<BusAttendanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "99"),
                        new Claim(ClaimTypes.Role, AppRoles.Admin)
                    }, "test"))
                }
            }
        };
        var busEvent = new BusEventRequest { StudentId = 1, DriverId = 1, BusId = 1, Latitude = 33, Longitude = 71 };
        Assert.IsType<OkObjectResult>((await controller.PickIn(busEvent, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.PickIn(busEvent, default)).Result); // Debounced.

        var send = Assert.Single(fcm.Sends);
        Assert.Equal(new[] { "linked-device" }, send.Tokens);
        Assert.Equal("bus_event", send.Push.Data!["type"]);
        Assert.Equal("1", send.Push.Data["studentId"]);
        Assert.Contains("baith chuka hai", send.Push.Body); // The linked parent's language.
        Assert.Equal(TimeSpan.FromMinutes(10), send.Push.TimeToLive); // Never arrive hours late.
        var attendance = Assert.Single(await db.BusAttendances.ToListAsync());
        var whatsApp = await db.WhatsAppLogs.SingleAsync();
        Assert.Equal(WhatsAppStatus.Held, whatsApp.Status);
        Assert.Equal(attendance.Id, whatsApp.BusAttendanceId);

        // An offline event remains in history but must not produce a dangerously
        // late closed-app notification when it finally reaches the server.
        busEvent.TimestampUtc = DateTime.UtcNow.AddMinutes(-10);
        Assert.IsType<OkObjectResult>((await controller.DropOut(busEvent, default)).Result);
        Assert.Single(fcm.Sends);
        Assert.Equal(2, await db.BusAttendances.CountAsync());
        Assert.Contains(await db.WhatsAppLogs.ToListAsync(), row => row.Status == WhatsAppStatus.Expired);
    }

    [Fact]
    public async Task AndroidClientJsonIsNotReportedAsAnAdminSdkServerCredential()
    {
        await using var db = CreateDb();
        var path = Path.Combine(Path.GetTempPath(), $"firebase-client-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path,
            "{\"project_info\":{\"project_id\":\"queen-school\"},\"client\":[]}");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:Enabled"] = "true",
                ["Firebase:CredentialsJsonPath"] = path
            }).Build();
            var controller = new DevicesController(
                new UnitOfWork(db), configuration, NullLogger<DevicesController>.Instance);
            var result = Assert.IsType<OkObjectResult>((await controller.PushStatus(default)).Result);
            var envelope = Assert.IsType<ApiResponse<PushServiceStatus>>(result.Value);
            Assert.True(envelope.Data!.CredentialsPresent);
            Assert.False(envelope.Data.CredentialsValid);
            Assert.False(envelope.Data.Ready);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParentLinksRejectNonParentInactiveAndMissingStudent()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            new User { Id = 1, Username = "staff", Role = UserRole.Admin },
            new User { Id = 2, Username = "inactive", Role = UserRole.Parent, IsActive = false },
            new User { Id = 3, Username = "parent", Role = UserRole.Parent });
        await db.SaveChangesAsync();
        var links = new ParentLinksController(new UnitOfWork(db));

        Assert.IsType<NotFoundObjectResult>((await links.Link(new() { ParentUsername = "staff", StudentId = 1 }, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await links.Link(new() { ParentUsername = "inactive", StudentId = 1 }, default)).Result);
        Assert.IsType<NotFoundObjectResult>((await links.Link(new() { ParentUsername = "parent", StudentId = 1 }, default)).Result);
        Assert.Empty(await db.ParentStudents.ToListAsync());
    }

    private sealed class CaptureFcm : IFcmSender
    {
        public List<(string[] Tokens, FcmPush Push)> Sends { get; } = new();
        public Task<int> SendToDevicesAsync(IReadOnlyList<string> tokens, FcmPush push, CancellationToken ct = default)
        {
            Sends.Add((tokens.ToArray(), push));
            return Task.FromResult(tokens.Count);
        }
    }

    private sealed class FixedGeocoder : IGeocodingService
    {
        public Task<string> GetLocationLabelAsync(double lat, double lng, CancellationToken ct = default)
            => Task.FromResult("Test location");
    }

    private sealed class NoopNotifications : ISchoolNotificationService
    {
        public Task SendPickInAlertAsync(BusEventNotification n, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendDropOutAlertAsync(BusEventNotification n, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendTripUpdateAsync(TripNotification n, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBusLocationAsync(BusLocationUpdate n, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAdminAlertAsync(AdminAlertNotification n, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAnnouncementAsync(AnnouncementNotification n, CancellationToken ct = default) => Task.CompletedTask;
    }
}
