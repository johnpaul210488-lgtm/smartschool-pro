using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>
/// Twilio WhatsApp sender over plain HTTPS (POST .../Messages.json).
/// Twilio:Enabled=false = DRY-RUN (logged, reported Sent) so the whole pipeline
/// is testable without spending credit. Swappable for the official Twilio SDK
/// without touching callers.
/// </summary>
public sealed class TwilioWhatsAppService : IWhatsAppSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<TwilioWhatsAppService> _logger;

    public TwilioWhatsAppService(HttpClient http, IConfiguration config, ILogger<TwilioWhatsAppService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct = default)
    {
        var to = PhoneNormalizer.ToWhatsAppAddress(toPhone);
        if (to is null)
            return WhatsAppSendResult.Fail($"Invalid phone number '{toPhone}'.");

        if (!_config.GetValue("Twilio:Enabled", false))
        {
            _logger.LogInformation("[WhatsApp DRY-RUN] To={To} | {Body}", to, body);
            return WhatsAppSendResult.Ok($"DRYRUN-{Guid.NewGuid():N}");
        }

        var sid = _config["Twilio:AccountSid"];
        var token = _config["Twilio:AuthToken"];
        var from = _config["Twilio:FromWhatsAppNumber"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
            return WhatsAppSendResult.Fail("Twilio is enabled but AccountSid/AuthToken/FromWhatsAppNumber are missing.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = from,
            ["Body"] = body
        });

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            string? messageSid = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                messageSid = doc.RootElement.GetProperty("sid").GetString();
            }
            catch (JsonException) { /* SID parsing is best-effort */ }

            return WhatsAppSendResult.Ok(messageSid ?? "unknown");
        }

        return WhatsAppSendResult.Fail($"Twilio HTTP {(int)response.StatusCode}: {payload}");
    }
}
