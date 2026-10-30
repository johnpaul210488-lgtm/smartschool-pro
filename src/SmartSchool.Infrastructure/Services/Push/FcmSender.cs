using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;

namespace SmartSchool.Infrastructure.Services.Push;

/// <summary>
/// FCM v1 sender via the official Firebase Admin SDK. When Firebase:Enabled is
/// false, messages are logged only and zero deliveries are reported.
/// Setup docs: docs/deployment-guide.md → "Firebase Cloud Messaging".
/// </summary>
public sealed class FirebaseFcmSender : IFcmSender
{
    // Firebase's multicast API accepts at most 500 targets per call.
    private const int MaxTokensPerBatch = 500;
    private readonly ILogger<FirebaseFcmSender> _logger;
    private readonly bool _enabled;
    private readonly Lazy<FirebaseMessaging?> _messaging;

    public FirebaseFcmSender(IConfiguration config, ILogger<FirebaseFcmSender> logger)
    {
        _logger = logger;
        _enabled = config.GetValue("Firebase:Enabled", false);
        var path = config["Firebase:CredentialsJsonPath"];

        if (!_enabled)
            logger.LogWarning("FCM is disabled (Firebase:Enabled=false). Parent push notifications will NOT be delivered.");
        else if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            logger.LogWarning("FCM is enabled but Firebase:CredentialsJsonPath is missing or the file does not exist. Pushes will fail.");
        else
            logger.LogInformation("FCM is enabled and a credentials file is present. Delivery still depends on valid credentials and registered devices.");

        _messaging = new Lazy<FirebaseMessaging?>(() =>
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

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
            _logger.LogWarning("[FCM DRY-RUN] {Count} device(s); no push was sent | {Title}", tokens.Count, push.Title);
            return 0;
        }

        FirebaseMessaging? messaging;
        try
        {
            messaging = _messaging.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM could not initialize from the configured service-account credentials.");
            return 0;
        }
        if (messaging is null)
        {
            _logger.LogWarning("FCM cannot send: service-account credentials file is missing.");
            return 0;
        }

        var accepted = 0;
        var offset = 0;
        foreach (var message in CreateMessages(tokens, push))
        {
            ct.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // FlutterFire getToken() returns registration tokens, not Firebase Installation IDs.
            var batchCount = message.Tokens.Count;
#pragma warning restore CS0618
            try
            {
                var response = await messaging.SendEachForMulticastAsync(message, ct);
                accepted += response.SuccessCount;
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    if (response.Responses[i].Exception is { } error)
                        _logger.LogWarning("FCM target[{Index}] failed: {FcmCode} (API code {ApiCode})",
                            offset + i, error.MessagingErrorCode?.ToString() ?? "unknown", error.ErrorCode);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // A failure in one batch must not prevent later batches being sent.
                _logger.LogWarning(ex, "FCM multicast failed for {Count} device(s), starting at index {Offset}", batchCount, offset);
            }
            offset += batchCount;
        }

        _logger.LogInformation("FCM accepted {Ok}/{Total} device(s) | {Title}", accepted, tokens.Count, push.Title);
        return accepted;
    }

    internal static IEnumerable<MulticastMessage> CreateMessages(IReadOnlyList<string> tokens, FcmPush push)
    {
        foreach (var batch in tokens.Chunk(MaxTokensPerBatch))
        {
            yield return new MulticastMessage
            {
                // getToken()/onTokenRefresh supply FCM registration tokens. Setting
                // Fids instead serializes them as "fid" and targets the wrong ID type.
#pragma warning disable CS0618
                Tokens = batch,
#pragma warning restore CS0618
                Notification = new Notification { Title = push.Title, Body = push.Body },
                Data = push.Data is null ? null : new Dictionary<string, string>(push.Data),
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    // Time-sensitive pick/drop alerts must not appear hours later
                    // after a phone comes back online.
                    TimeToLive = push.TimeToLive,
                    Notification = new AndroidNotification { ChannelId = "smartschool_alerts", Sound = "default" }
                },
                Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } }
            };
        }
    }
}
