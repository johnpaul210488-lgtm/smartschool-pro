using System.Reflection;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Infrastructure.Services.Push;
using SmartSchool.Infrastructure.Services.WhatsApp;
using SmartSchool.Shared.Enums;
using Xunit;

namespace SmartSchool.Tests;

public sealed class NotificationSenderTests
{
    [Fact]
    public void FirebaseUsesRegistrationTokensNotInstallationIdsAndBatchesAt500()
    {
        var tokens = Enumerable.Range(0, 1001).Select(i => $"registration-token-{i}").ToArray();
        var push = new FcmPush(
            "Title", "Body", new Dictionary<string, string> { ["eventId"] = "42" },
            TimeSpan.FromMinutes(10));
        var batches = FirebaseFcmSender.CreateMessages(tokens, push).ToArray();

#pragma warning disable CS0618 // Registration tokens are deprecated in the SDK, but are what FlutterFire supplies.
        Assert.Equal(new[] { 500, 500, 1 }, batches.Select(b => b.Tokens.Count));
        Assert.Equal(tokens, batches.SelectMany(b => b.Tokens));
        // Exercise the pinned SDK's multicast expansion too: the wire target
        // must be Message.Token, not Message.Fid.
        var expand = typeof(MulticastMessage).GetMethod("GetMessageList", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sdkMessages = Assert.IsAssignableFrom<IEnumerable<Message>>(expand.Invoke(batches[0], null));
        var firstTarget = sdkMessages.First();
        Assert.Equal(tokens[0], firstTarget.Token);
        Assert.Null(firstTarget.Fid);
#pragma warning restore CS0618
        Assert.All(batches, b =>
        {
            Assert.Null(b.Fids);
            Assert.Equal("Title", b.Notification.Title);
            Assert.Equal("42", b.Data["eventId"]);
            Assert.Equal("smartschool_alerts", b.Android.Notification.ChannelId);
            Assert.Equal(TimeSpan.FromMinutes(10), b.Android.TimeToLive);
        });
    }

    [Fact]
    public async Task DisabledOrMissingFirebaseNeverReportsDryRunAsDelivered()
    {
        var disabled = new FirebaseFcmSender(new ConfigurationBuilder().Build(), NullLogger<FirebaseFcmSender>.Instance);
        var missing = new FirebaseFcmSender(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Firebase:Enabled"] = "true",
            ["Firebase:CredentialsJsonPath"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")
        }).Build(), NullLogger<FirebaseFcmSender>.Instance);

        Assert.Equal(0, await disabled.SendToDevicesAsync(new[] { "token" }, new FcmPush("t", "b")));
        Assert.Equal(0, await missing.SendToDevicesAsync(new[] { "token" }, new FcmPush("t", "b")));
    }
}
