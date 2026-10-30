using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>Official Twilio WhatsApp provider. Provider selection/dry-run lives outside this class.</summary>
public sealed class TwilioWhatsAppSender
{
    private static readonly HashSet<int> PermanentTwilioCodes =
    [
        21211, // invalid To number
        21610, // recipient opted out
        21614, // not a valid mobile number
        63024  // invalid WhatsApp recipient
    ];

    private readonly IConfiguration _config;
    private readonly ILogger<TwilioWhatsAppSender> _logger;

    public TwilioWhatsAppSender(IConfiguration config, ILogger<TwilioWhatsAppSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<WhatsAppSendResult> SendAsync(
        Guid correlationId,
        string toPhone,
        string body,
        CancellationToken ct = default)
    {
        var to = PhoneNormalizer.ToWhatsAppAddress(toPhone);
        if (to is null)
            return WhatsAppSendResult.Fail(
                $"Invalid phone number '{toPhone}'.",
                WhatsAppFailureKind.Permanent,
                retryable: false);

        var sid = _config["Twilio:AccountSid"];
        var token = _config["Twilio:AuthToken"];
        var from = _config["Twilio:WhatsAppFrom"] ?? _config["Twilio:FromWhatsAppNumber"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
            return WhatsAppSendResult.Fail(
                "Twilio is selected but AccountSid/AuthToken/WhatsAppFrom is missing.",
                WhatsAppFailureKind.Configuration,
                retryable: false);

        try
        {
            ct.ThrowIfCancellationRequested();
            TwilioClient.Init(sid, token);
            var message = await MessageResource.CreateAsync(
                to: new PhoneNumber(to),
                from: new PhoneNumber(from),
                body: body);

            return WhatsAppSendResult.Ok(message.Sid ?? $"TWILIO-{correlationId:N}");
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Twilio API error {Code} sending WhatsApp correlation {CorrelationId}",
                ex.Code, correlationId);
            var code = ex.Code;
            if (PermanentTwilioCodes.Contains(code))
                return WhatsAppSendResult.Fail(
                    $"Twilio API {code}: provider rejected this recipient/message.",
                    WhatsAppFailureKind.Permanent,
                    retryable: false);
            if (code == 20429)
                return WhatsAppSendResult.Fail(
                    $"Twilio API {code}: rate limited.",
                    WhatsAppFailureKind.RateLimited,
                    retryable: true,
                    retryAfterSeconds: 30);

            return WhatsAppSendResult.Fail(
                $"Twilio API {code}: temporary provider failure.",
                WhatsAppFailureKind.Transient,
                retryable: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twilio WhatsApp send failed for correlation {CorrelationId}", correlationId);
            return WhatsAppSendResult.Fail(
                "Twilio provider is temporarily unavailable.",
                WhatsAppFailureKind.ProviderUnavailable,
                retryable: true,
                retryAfterSeconds: 15);
        }
    }
}
