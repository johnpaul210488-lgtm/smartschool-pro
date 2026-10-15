using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;

namespace SmartSchool.Infrastructure.Services.Push;

/// <summary>
/// FCM v1 sender via the official Firebase Admin SDK.
/// Firebase:Enabled=false → DRY-RUN (logged, reported as sent), so the whole
/// Driver→API→Push chain is testable before a Firebase project exists.
/// Setup docs: docs/deployment-guide.md → "Firebase Cloud Messaging".
/// </summary>
public sealed class FirebaseFcmSender : IFcmSender
{
    private readonly ILogger<FirebaseFcmSender> _logger;
    private readonly bool _enabled;
    private readonly Lazy<FirebaseMessaging?> _messaging;

    public FirebaseFcmSender(IConfiguration config, ILogger<FirebaseFcmSender> logger)
    {
        _logger = logger;
        _enabled = config.GetValue("Firebase:Enabled", false);

        _messaging = new Lazy<FirebaseMessaging?>(() =>
        {
            var path = config["Firebase:CredentialsJsonPath"];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                logger.LogWarning("Firebase:Enabled=true but CredentialsJsonPath '{Path}' is missing - pushes will fail.", path);
                return null;
            }
            var credential = CredentialFactory
                .FromFile<Google.Apis.Auth.OAuth2.ServiceAccountCredential>(path)
                .ToGoogleCredential();
            var app = FirebaseApp.DefaultInstance
                ?? FirebaseApp.Create(new AppOptions { Credential = credential });
            return FirebaseMessaging.GetMessaging(app);
        });
    }

    public async Task<int> SendToDevicesAsync(IReadOnlyList<string> tokens, FcmPush push, CancellationToken ct = default)
    {
        if (tokens.Count == 0) return 0;

        if (!_enabled)
        {
            _logger.LogInformation("[FCM DRY-RUN] {Count} device(s) | {Title} | {Body}", tokens.Count, push.Title, push.Body);
            return tokens.Count;
        }

        var messaging = _messaging.Value;
        if (messaging is null)
            return 0;

        var message = new MulticastMessage
        {
            Fids = tokens.ToList(),
            Notification = new Notification { Title = push.Title, Body = push.Body },
            Data = push.Data is null ? null : new Dictionary<string, string>(push.Data),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification { ChannelId = "smartschool_alerts", Sound = "default" }
            },
            Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } }
        };

        try
        {
            var response = await messaging.SendEachForMulticastAsync(message, ct);
            if (response.FailureCount > 0)
            {
                foreach (var (resp, i) in response.Responses.Select((r, i) => (r, i)))
                    if (resp.Exception is not null)
                        _logger.LogWarning("FCM token[{I}] failed: {Err}", i, resp.Exception.Message);
            }
            _logger.LogInformation("FCM sent {Ok}/{Total} | {Title}", response.SuccessCount, tokens.Count, push.Title);
            return response.SuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM multicast failed for {Count} token(s)", tokens.Count);
            return 0;
        }
    }
}
