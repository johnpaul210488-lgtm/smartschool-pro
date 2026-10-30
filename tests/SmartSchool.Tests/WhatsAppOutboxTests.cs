using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Enums;
using Xunit;

namespace SmartSchool.Tests;

public sealed class WhatsAppOutboxTests
{
    private sealed class Activation : IWhatsAppActivation { public DateTime? ActivatedAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(-2); }
    private sealed class FakeSender : IWhatsAppSender
    {
        public int Calls;
        public WhatsAppSendOutcome Outcome = WhatsAppSendOutcome.Submitted;
        public async Task<WhatsAppSendResult> SendAsync(WhatsAppLog log, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(10, ct);
            return new(Outcome, Outcome == WhatsAppSendOutcome.Submitted ? "provider-id" : null);
        }
    }
    private static (ServiceProvider Provider, WhatsAppQueueHostedService Worker, Activation Activation, FakeSender Sender) Create(string? sql = null)
    {
        var activation = new Activation();
        var sender = new FakeSender();
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<SchoolDbContext>(o => { if (sql is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(sql); });
        services.AddSingleton<IWhatsAppActivation>(activation);
        services.AddSingleton<IWhatsAppSender>(sender);
        services.AddSingleton<InMemoryWhatsAppQueue>();
        var provider = services.BuildServiceProvider();
        var worker = new WhatsAppQueueHostedService(provider.GetRequiredService<InMemoryWhatsAppQueue>(),
            provider.GetRequiredService<IServiceScopeFactory>(), activation, NullLogger<WhatsAppQueueHostedService>.Instance);
        return (provider, worker, activation, sender);
    }
    private static async Task<(int StudentId, Guid Id)> Seed(ServiceProvider provider, WhatsAppStatus status = WhatsAppStatus.PendingQr,
        bool consent = true, bool optOut = false, DateTime? created = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        // SQL fixtures have unique names; no IDs are assumed.
        var schoolClass = new SchoolClass { Name = Guid.NewGuid().ToString("N")[..8], Section = "T" };
        var student = new Student { FullName = "Outbox test", Class = schoolClass, FatherPhone = "03001234567", FatherName = "Test", RollNo = Guid.NewGuid().ToString("N")[..8], Address = "Test" };
        db.Students.Add(student);
        if (consent) db.WhatsAppConsents.Add(new WhatsAppConsent { Student = student, PhoneE164 = "+923001234567", Granted = true, ChangedAtUtc = DateTime.UtcNow });
        if (optOut)
        {
            var parent = new User { Username = Guid.NewGuid().ToString("N"), FullName = "Parent", PasswordHash = "test", Role = UserRole.Parent, Phone = "03001234567", ReceiveWhatsApp = false };
            db.ParentStudents.Add(new ParentStudent { ParentUser = parent, Student = student });
        }
        var id = Guid.NewGuid();
        db.WhatsAppLogs.Add(new WhatsAppLog { Student = student, CorrelationId = id, ParentPhone = "03001234567", Message = "Test", Status = status, CreatedAtUtc = created ?? DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (student.Id, id);
    }
    private static async Task<WhatsAppLog> Log(ServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SchoolDbContext>().WhatsAppLogs.AsNoTracking().SingleAsync(l => l.CorrelationId == id);
    }

    [Fact]
    public async Task OnlyFreshConsentedQrMessagesAreSubmittedAndNeverClaimedDelivered()
    {
        var (p, worker, _, sender) = Create(); using (p) using (worker)
        {
            var (_, id) = await Seed(p);
            await worker.ProcessAsync(id); await worker.ProcessAsync(id);
            var log = await Log(p, id);
            Assert.Equal(WhatsAppStatus.Submitted, log.Status);
            Assert.Null(log.SentAtUtc);
            Assert.Equal(1, sender.Calls);
        }
    }

    [Fact]
    public async Task HistoricalAndAmbiguousStatesAreNeverSentOrReplayed()
    {
        var (p, worker, _, sender) = Create(); using (p) using (worker)
        {
            foreach (var status in new[] { WhatsAppStatus.Queued, WhatsAppStatus.Unknown, WhatsAppStatus.Submitted, WhatsAppStatus.Held, WhatsAppStatus.Sending })
            {
                var (_, id) = await Seed(p, status);
                await worker.ProcessAsync(id);
                Assert.Equal(status, (await Log(p, id)).Status);
            }
            Assert.Equal(0, sender.Calls);
        }
    }

    [Fact]
    public async Task DisabledStaleRevokedAndOptedOutNeverReachSender()
    {
        var (p, worker, active, sender) = Create(); using (p) using (worker)
        {
            var (_, noConsent) = await Seed(p, consent: false);
            var (_, optOut) = await Seed(p, optOut: true);
            await worker.ProcessAsync(noConsent); await worker.ProcessAsync(optOut);
            Assert.Equal(WhatsAppStatus.Held, (await Log(p, noConsent)).Status);
            Assert.Equal(WhatsAppStatus.Held, (await Log(p, optOut)).Status);
            active.ActivatedAtUtc = DateTime.UtcNow.AddMinutes(-30);
            var (_, stale) = await Seed(p, created: DateTime.UtcNow.AddMinutes(-6));
            await worker.ProcessAsync(stale);
            Assert.Equal(WhatsAppStatus.Expired, (await Log(p, stale)).Status);
            var (_, disabled) = await Seed(p); active.ActivatedAtUtc = null;
            await worker.ProcessAsync(disabled);
            Assert.Equal(WhatsAppStatus.Held, (await Log(p, disabled)).Status);
            Assert.Equal(0, sender.Calls);
        }
    }

    [Fact]
    public async Task UnknownIsNeverRetriedWhileKnownNotSentMayRetryWithinFreshWindow()
    {
        var (p, worker, _, sender) = Create(); using (p) using (worker)
        {
            var (_, uncertain) = await Seed(p); sender.Outcome = WhatsAppSendOutcome.Unknown;
            await worker.ProcessAsync(uncertain); await worker.ProcessAsync(uncertain);
            Assert.Equal(WhatsAppStatus.Unknown, (await Log(p, uncertain)).Status);
            Assert.Equal(1, sender.Calls);
            var (_, notSent) = await Seed(p); sender.Outcome = WhatsAppSendOutcome.NotSent;
            await worker.ProcessAsync(notSent);
            Assert.Equal(WhatsAppStatus.PendingQr, (await Log(p, notSent)).Status);
            using (var scope = p.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
                var l = await db.WhatsAppLogs.SingleAsync(x => x.CorrelationId == notSent);
                l.LastAttemptAtUtc = DateTime.UtcNow.AddSeconds(-20); await db.SaveChangesAsync();
            }
            sender.Outcome = WhatsAppSendOutcome.Submitted; await worker.ProcessAsync(notSent);
            Assert.Equal(WhatsAppStatus.Submitted, (await Log(p, notSent)).Status);
            Assert.Equal(3, sender.Calls);
        }
    }

    [Fact]
    public void NewEventsMustBeAfterActivationAndNoMoreThanFiveMinutesOld()
    {
        var now = DateTime.UtcNow; var activated = now.AddMinutes(-10);
        Assert.False(WhatsAppOutboxRules.IsFresh(null, now, now));
        Assert.False(WhatsAppOutboxRules.IsFresh(activated, now.AddMinutes(-6), now));
        Assert.False(WhatsAppOutboxRules.IsFresh(now, now.AddSeconds(-1), now));
        Assert.True(WhatsAppOutboxRules.IsFresh(activated, now, now));
    }

    // Runs in branch CI against SQL Server after migrations have been applied.
    [Fact]
    public async Task SqlAtomicClaimPreventsConcurrentDuplicateSubmission()
    {
        var connection = Environment.GetEnvironmentVariable("SQL_WHATSAPP_TEST_CONNECTION");
        if (string.IsNullOrEmpty(connection)) return;
        var (p, worker, active, sender) = Create(connection); using (p) using (worker)
        {
            var (_, id) = await Seed(p);
            var second = new WhatsAppQueueHostedService(p.GetRequiredService<InMemoryWhatsAppQueue>(),
                p.GetRequiredService<IServiceScopeFactory>(), active, NullLogger<WhatsAppQueueHostedService>.Instance);
            using (second) await Task.WhenAll(worker.ProcessAsync(id), second.ProcessAsync(id));
            Assert.Equal(1, sender.Calls);
            Assert.Equal(WhatsAppStatus.Submitted, (await Log(p, id)).Status);
        }
    }
}
