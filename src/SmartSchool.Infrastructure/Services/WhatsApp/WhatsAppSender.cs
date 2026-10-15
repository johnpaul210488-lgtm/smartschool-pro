using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>
/// Twilio WhatsApp sender using the official Twilio SDK (Twilio.AspNet.Core).
/// Twilio:Enabled=false = DRY-RUN (logged, reported Sent) so the whole pipeline
/// is testable without spending credit.
/// </summary>
public sealed class TwilioWhatsAppService : IWhatsAppSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<TwilioWhatsAppService> _logger;

    public TwilioWhatsAppService(IConfiguration config, ILogger<TwilioWhatsAppService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct = default)
    {
        var to = PhoneNormalizer.ToWhatsAppAddress(toPhone);
        if (to is null)
            return WhatsAppSendResult.Fail($"Invalid phone number '{toPhone}'.");

        if (!_config.GetValue("Twilio:Enabled", false))
        {
            _logger.LogInformation("[WhatsApp DRY-RUN] To={To} | {Body}", to, body.ReplaceLineEndings("\\n"));
            return WhatsAppSendResult.Ok($"DRYRUN-{Guid.NewGuid():N}");
        }

        // User-facing key name is "WhatsAppFrom"; older configs used "FromWhatsAppNumber".
        var sid = _config["Twilio:AccountSid"];
        var token = _config["Twilio:AuthToken"];
        var from = _config["Twilio:WhatsAppFrom"] ?? _config["Twilio:FromWhatsAppNumber"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
            return WhatsAppSendResult.Fail(
                "Twilio enabled but AccountSid/AuthToken/WhatsAppFrom missing in appsettings.json.");

        try
        {
            TwilioClient.Init(sid, token);
            var message = await MessageResource.CreateAsync(
                to: new PhoneNumber(to),
                from: new PhoneNumber(from),
                body: body);

            return WhatsAppSendResult.Ok(message.Sid ?? "unknown");
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Twilio API error sending to {To}", to);
            return WhatsAppSendResult.Fail($"Twilio API {ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twilio send failed to {To}", to);
            return WhatsAppSendResult.Fail(ex.Message);
        }
    }
}
