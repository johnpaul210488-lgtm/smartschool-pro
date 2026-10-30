using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Enums;
using Xunit;

namespace SmartSchool.Tests;

public sealed class WhatsAppOutboxTests
{
    private sealed class FakeSender : IWhatsAppSender
    {
        public int Calls;
        public WhatsAppSendResult Result = WhatsAppSendResult.Ok("provider-id");

        public async Task<WhatsAppSendResult> SendAsync(
            Guid correlationId,
            string toPhone,
            string body,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(20, ct);
            return Result;
        }
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

    private static (ServiceProvider Provider, WhatsAppQueueHostedService Worker, FakeSender Sender) Create(string? sql = null)
    {
        var sender = new FakeSender();
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<SchoolDbContext>(options =>
        {
            if (sql is null) options.UseInMemoryDatabase(databaseName);
            else options.UseSqlServer(sql);
        });
        var options = Options.Create(new WhatsAppOptions
        {
            Queue = new WhatsAppQueueOptions
            {
                Capacity = 100,
                MaxAttempts = 3,
                RetryWindowSeconds = 120,
                InitialRetryDelaySeconds = 1,
                SweepIntervalSeconds = 60
            }
        });
        services.AddSingleton<IOptions<WhatsAppOptions>>(options);
        services.AddSingleton<IWhatsAppSender>(sender);
        services.AddSingleton<ISchoolNotificationService, NoopNotifications>();
        services.AddSingleton<InMemoryWhatsAppQueue>();
        var provider = services.BuildServiceProvider();
        var worker = new WhatsAppQueueHostedService(
            provider.GetRequiredService<InMemoryWhatsAppQueue>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<WhatsAppQueueHostedService>.Instance);
        return (provider, worker, sender);
    }

    private static async Task<Guid> Seed(
        ServiceProvider provider,
        WhatsAppStatus status = WhatsAppStatus.Queued,
        bool consent = true,
        bool optOut = false,
        DateTime? created = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var schoolClass = new SchoolClass
        {
            Name = Guid.NewGuid().ToString("N")[..8],
            Section = "T"
        };
        var student = new Student
        {
            FullName = "Outbox test",
            Class = schoolClass,
            FatherPhone = "03001234567",
            FatherName = "Test",
            RollNo = Guid.NewGuid().ToString("N")[..8],
            Address = "Test"
        };
        db.Students.Add(student);
        if (consent)
        {
            db.WhatsAppConsents.Add(new WhatsAppConsent
            {
                Student = student,
                PhoneE164 = "+923001234567",
                Granted = true,
                ChangedAtUtc = DateTime.UtcNow
            });
        }
        if (optOut)
        {
            var parent = new User
            {
                Username = Guid.NewGuid().ToString("N"),
                FullName = "Parent",
                PasswordHash = "test",
                Role = UserRole.Parent,
                Phone = "03001234567",
                ReceiveWhatsApp = false
            };
            db.ParentStudents.Add(new ParentStudent { ParentUser = parent, Student = student });
        }
        var id = Guid.NewGuid();
        db.WhatsAppLogs.Add(new WhatsAppLog
        {
            Student = student,
            CorrelationId = id,
            ParentPhone = "+923001234567",
            Message = "Test",
            Status = status,
            CreatedAtUtc = created ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<WhatsAppLog> Log(ServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SchoolDbContext>()
            .WhatsAppLogs.AsNoTracking().SingleAsync(l => l.CorrelationId == id);
    }

    [Fact]
    public async Task FreshConsentedMessageIsSentOnce()
    {
        var (provider, worker, sender) = Create();
        using (provider)
        using (worker)
        {
            var id = await Seed(provider);
            await worker.ProcessAsync(id);
            await worker.ProcessAsync(id);
            var log = await Log(provider, id);
            Assert.Equal(WhatsAppStatus.Sent, log.Status);
            Assert.NotNull(log.SentAtUtc);
            Assert.Equal("provider-id", log.ProviderMessageId);
            Assert.Equal(1, sender.Calls);
        }
    }

    [Fact]
    public async Task LegacyAndAuditOnlyStatesAreInert()
    {
        var (provider, worker, sender) = Create();
        using (provider)
        using (worker)
        {
            foreach (var status in new[]
                     {
                         WhatsAppStatus.PendingQr, WhatsAppStatus.Unknown, WhatsAppStatus.Submitted,
                         WhatsAppStatus.Held, WhatsAppStatus.Expired, WhatsAppStatus.Sending
                     })
            {
                var id = await Seed(provider, status);
                await worker.ProcessAsync(id);
                Assert.Equal(status, (await Log(provider, id)).Status);
            }
            Assert.Equal(0, sender.Calls);
        }
    }

    [Fact]
    public async Task MissingConsentAndParentOptOutNeverReachProvider()
    {
        var (provider, worker, sender) = Create();
        using (provider)
        using (worker)
        {
            var noConsent = await Seed(provider, consent: false);
            var optedOut = await Seed(provider, optOut: true);
            await worker.ProcessAsync(noConsent);
            await worker.ProcessAsync(optedOut);
            Assert.Equal(WhatsAppStatus.Held, (await Log(provider, noConsent)).Status);
            Assert.Equal(WhatsAppStatus.Held, (await Log(provider, optedOut)).Status);
            Assert.Equal(0, sender.Calls);
        }
    }

    [Fact]
    public async Task KnownTransientFailureCanRetryButAmbiguousHandoffCannot()
    {
        var (provider, worker, sender) = Create();
        using (provider)
        using (worker)
        {
            var retryable = await Seed(provider);
            sender.Result = WhatsAppSendResult.Fail(
                "temporary", WhatsAppFailureKind.ProviderUnavailable, retryable: true, retryAfterSeconds: 1);
            await worker.ProcessAsync(retryable);
            Assert.Equal(WhatsAppStatus.Queued, (await Log(provider, retryable)).Status);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
                var row = await db.WhatsAppLogs.SingleAsync(l => l.CorrelationId == retryable);
                row.NextAttemptAtUtc = null;
                await db.SaveChangesAsync();
            }
            sender.Result = WhatsAppSendResult.Ok("after-retry");
            await worker.ProcessAsync(retryable);
            Assert.Equal(WhatsAppStatus.Sent, (await Log(provider, retryable)).Status);

            var ambiguous = await Seed(provider);
            sender.Result = WhatsAppSendResult.Fail(
                "unknown", WhatsAppFailureKind.DeliveryUnknown, retryable: false);
            await worker.ProcessAsync(ambiguous);
            await worker.ProcessAsync(ambiguous);
            var failed = await Log(provider, ambiguous);
            Assert.Equal(WhatsAppStatus.Failed, failed.Status);
            Assert.Equal(WhatsAppFailureKind.DeliveryUnknown.ToString(), failed.FailureCode);
            Assert.Equal(3, sender.Calls);
        }
    }

    [Fact]
    public void EventsOlderThanFiveMinutesAreNotFresh()
    {
        var now = DateTime.UtcNow;
        Assert.False(WhatsAppOutboxRules.IsFresh(now.AddMinutes(-6), now));
        Assert.False(WhatsAppOutboxRules.IsFresh(now.AddMinutes(2), now));
        Assert.True(WhatsAppOutboxRules.IsFresh(now, now));
    }

    // Runs in branch CI against SQL Server after migrations have been applied.
    [Fact]
    public async Task SqlAtomicClaimPreventsConcurrentDuplicateSubmission()
    {
        var connection = Environment.GetEnvironmentVariable("SQL_WHATSAPP_TEST_CONNECTION");
        if (string.IsNullOrEmpty(connection)) return;
        var (provider, worker, sender) = Create(connection);
        using (provider)
        using (worker)
        {
            var id = await Seed(provider);
            var options = provider.GetRequiredService<IOptions<WhatsAppOptions>>();
            using var second = new WhatsAppQueueHostedService(
                new InMemoryWhatsAppQueue(options),
                provider.GetRequiredService<IServiceScopeFactory>(),
                options,
                NullLogger<WhatsAppQueueHostedService>.Instance);
            await Task.WhenAll(worker.ProcessAsync(id), second.ProcessAsync(id));
            Assert.Equal(1, sender.Calls);
            Assert.Equal(WhatsAppStatus.Sent, (await Log(provider, id)).Status);
        }
    }
}
