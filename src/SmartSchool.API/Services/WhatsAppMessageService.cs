using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Services;

/// <summary>
/// Sends WhatsApp messages via the local Baileys Node.js gateway (HTTP API).
/// Disabled when SMARTSCHOOL_WHATSAPP_ENABLED=false (no messages are sent,
/// only logged). Gateway runs on 127.0.0.1 only — never exposed to browsers.
/// </summary>
public sealed class WhatsAppMessageService : IWhatsAppMessageService
{
    private readonly HttpClient? _http;
    private readonly ILogger<WhatsAppMessageService> _logger;
    private readonly string? _baseUrl;
    private readonly string? _secret;
    private readonly bool _enabled;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public WhatsAppMessageService(IConfiguration config, ILogger<WhatsAppMessageService> logger)
    {
        _logger = logger;
        _enabled = config.GetValue("SmartSchool:WhatsApp:Enabled", false);
        _baseUrl = config["SmartSchool:WhatsApp:BaseUrl"] ?? "http://127.0.0.1:3001";
        _secret = config["SmartSchool:WhatsApp:Secret"];

        if (!_enabled)
        {
            _logger.LogWarning("WhatsApp messaging is disabled (SmartSchool:WhatsAppEnabled=false).");
        }
        else if (string.IsNullOrWhiteSpace(_secret))
        {
            _logger.LogWarning("WhatsApp enabled but SMARTSCHOOL_WHATSAPP_SECRET is not set — messages will not be sent.");
        }
        else
        {
            _http = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(30),
                BaseAddress = new Uri(_baseUrl!),
            };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
            _logger.LogInformation("WhatsApp messaging enabled → {BaseUrl}", _baseUrl);
        }
    }

    public Task<bool> IsEnabled() => Task.FromResult(_enabled && _secret is not null && _http is not null);

    public async Task<WhatsAppSendResponse> SendText(string to, string text, int? priority = null, CancellationToken ct = default)
    {
        if (!_enabled || _http is null)
            return new(false, null, "disabled", "WhatsApp messaging is disabled.");

        var body = new { to, text = text.Trim(), priority = priority ?? 10 };
        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/messages/text", body, _json, ct);
            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
            var ok = resp.IsSuccessStatusCode && result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            var id = result.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var status = result.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
            _logger.LogInformation("WhatsApp text to {To}: ok={Ok} id={Id}", to, ok, id);
            return new(ok, id, status, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp text to {To} failed.", to);
            return new(false, null, "error", ex.Message);
        }
    }

    public async Task<WhatsAppSendResponse> SendMedia(string to, string type, string path, string? fileName, string? caption, int? priority = null, CancellationToken ct = default)
    {
        if (!_enabled || _http is null)
            return new(false, null, "disabled", "WhatsApp messaging is disabled.");

        var body = new { to, type, path, fileName, caption, priority = priority ?? 10 };
        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/messages/media", body, _json, ct);
            var result = await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
            var ok = resp.IsSuccessStatusCode && result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            var id = result.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var status = result.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
            _logger.LogInformation("WhatsApp media to {To}: ok={Ok} id={Id}", to, ok, id);
            return new(ok, id, status, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp media to {To} failed.", to);
            return new(false, null, "error", ex.Message);
        }
    }

    public async Task<dynamic?> GetStatus(CancellationToken ct = default)
    {
        if (!_enabled || _http is null) return null;
        try
        {
            using var resp = await _http.GetAsync("/api/status", ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<JsonElement>(_json, ct);
        }
        catch
        {
            return null;
        }
    }
}
