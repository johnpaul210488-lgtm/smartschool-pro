using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>Authenticated loopback client for the isolated Baileys sidecar.</summary>
public sealed class LinkedDeviceWhatsAppClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly LinkedDeviceOptions _options;
    private readonly ILogger<LinkedDeviceWhatsAppClient> _logger;
    private readonly object _secretGate = new();
    private DateTime _secretLastWriteUtc;
    private string? _cachedApiKey;

    public LinkedDeviceWhatsAppClient(
        HttpClient http,
        IOptions<WhatsAppOptions> options,
        ILogger<LinkedDeviceWhatsAppClient> logger)
    {
        _http = http;
        _options = options.Value.LinkedDevice;
        _logger = logger;
    }

    public async Task<WhatsAppSendResult> SendAsync(
        Guid correlationId,
        string toPhone,
        string body,
        CancellationToken ct = default)
    {
        var to = PhoneNormalizer.ToE164(toPhone);
        if (to is null)
            return WhatsAppSendResult.Fail(
                $"Invalid phone number '{toPhone}'.",
                WhatsAppFailureKind.Permanent,
                retryable: false);

        try
        {
            using var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                "v1/messages",
                new
                {
                    to,
                    text = body,
                    idempotencyKey = correlationId.ToString("N"),
                    classification = "transactional"
                },
                ct);
            var envelope = await ReadEnvelopeAsync<GatewaySendData>(response, ct);
            if (response.IsSuccessStatusCode && envelope.Success && envelope.Data?.ProviderMessageId is { Length: > 0 } id)
                return WhatsAppSendResult.Ok(id);

            return MapSendFailure(response.StatusCode, envelope.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Linked-device gateway timed out for correlation {CorrelationId}; retry uses same idempotency key.", correlationId);
            return WhatsAppSendResult.Fail(
                "Linked-device gateway timed out; delivery will be reconciled by idempotency key.",
                WhatsAppFailureKind.ProviderUnavailable,
                retryable: true,
                retryAfterSeconds: 15);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Linked-device gateway unavailable for correlation {CorrelationId}", correlationId);
            return WhatsAppSendResult.Fail(
                "Linked-device gateway is unavailable or not configured.",
                WhatsAppFailureKind.ProviderUnavailable,
                retryable: true,
                retryAfterSeconds: 15);
        }
    }

    public async Task<WhatsAppSessionInfo> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "v1/session", null, ct);
            var envelope = await ReadEnvelopeAsync<GatewaySessionData>(response, ct);
            if (response.IsSuccessStatusCode && envelope.Success && envelope.Data is not null)
                return ToSessionInfo(envelope.Data, isAvailable: true);

            return Unavailable(envelope.Error?.Message ?? $"Gateway returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Linked-device status is unavailable.");
            return Unavailable(
                "Linked-device gateway is not running yet. The API supervisor will keep trying to start it.");
        }
    }

    public async Task<WhatsAppSessionCommandResult> ConnectAsync(
        bool acceptTermsAndRisk,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                "v1/session/connect",
                new { acceptTermsAndRisk },
                ct);
            var envelope = await ReadEnvelopeAsync<GatewaySessionData>(response, ct);
            var status = await GetStatusAsync(ct);
            return response.IsSuccessStatusCode && envelope.Success
                ? new WhatsAppSessionCommandResult(true, status, null)
                : new WhatsAppSessionCommandResult(false, status, envelope.Error?.Message ?? "Connection could not start.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start linked-device connection.");
            var status = Unavailable("Linked-device gateway is unavailable.");
            return new WhatsAppSessionCommandResult(false, status, status.LastError);
        }
    }

    public async Task<WhatsAppSessionCommandResult> LogoutAsync(
        bool confirmUnlink,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                "v1/session/logout",
                new { confirmUnlink },
                ct);
            var envelope = await ReadEnvelopeAsync<GatewaySessionData>(response, ct);
            var status = await GetStatusAsync(ct);
            return response.IsSuccessStatusCode && envelope.Success
                ? new WhatsAppSessionCommandResult(true, status, null)
                : new WhatsAppSessionCommandResult(false, status, envelope.Error?.Message ?? "Device could not be unlinked.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not unlink linked-device session.");
            var status = Unavailable("Linked-device gateway is unavailable.");
            return new WhatsAppSessionCommandResult(false, status, status.LastError);
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken ct)
    {
        var apiKey = LoadApiKey();
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private string LoadApiKey()
    {
        var file = WhatsAppPathResolver.Resolve(_options.SecretsFile);
        lock (_secretGate)
        {
            var lastWrite = File.GetLastWriteTimeUtc(file);
            if (_cachedApiKey is not null && lastWrite == _secretLastWriteUtc) return _cachedApiKey;

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var apiKey = document.RootElement.GetProperty("apiKey").GetString();
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
                throw new InvalidOperationException($"Gateway apiKey is missing from {file}.");

            _cachedApiKey = apiKey;
            _secretLastWriteUtc = lastWrite;
            return apiKey;
        }
    }

    private static async Task<GatewayEnvelope<T>> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var envelope = await response.Content.ReadFromJsonAsync<GatewayEnvelope<T>>(JsonOptions, ct);
        return envelope ?? new GatewayEnvelope<T>
        {
            Success = false,
            Error = new GatewayErrorData { Code = "invalid_gateway_response", Message = "Gateway returned an empty response." }
        };
    }

    private static WhatsAppSendResult MapSendFailure(HttpStatusCode status, GatewayErrorData? error)
    {
        var code = error?.Code ?? "gateway_error";
        var message = error?.Message ?? $"Linked-device gateway returned HTTP {(int)status}.";
        var retryAfter = error?.RetryAfterSeconds;
        return code switch
        {
            "session_disconnected" => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.SessionDisconnected, true, retryAfter ?? 15),
            "daily_safety_limit" or "recipient_safety_limit" or "gateway_queue_full" => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.RateLimited, error?.Retryable ?? true, retryAfter),
            "delivery_unknown" => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.DeliveryUnknown, false),
            "invalid_recipient" or "invalid_message" or "unsupported_message_classification" => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.Permanent, false),
            "unauthorized" => WhatsAppSendResult.Fail(
                "Linked-device gateway authentication failed.", WhatsAppFailureKind.Configuration, false),
            _ when (int)status >= 500 => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.ProviderUnavailable, true, retryAfter ?? 15),
            _ => WhatsAppSendResult.Fail(
                message, WhatsAppFailureKind.Permanent, error?.Retryable ?? false, retryAfter)
        };
    }

    private static WhatsAppSessionInfo ToSessionInfo(GatewaySessionData data, bool isAvailable)
        => new(
            data.Provider ?? WhatsAppProviders.LinkedDevice,
            data.Status ?? "unknown",
            isAvailable,
            data.IsConnected,
            data.HasSavedSession,
            data.ConnectedPhone,
            data.QrDataUrl,
            data.QrExpiresAtUtc,
            data.LastError,
            data.ReconnectAttempt,
            data.Safety is null
                ? null
                : new WhatsAppSafetyStatus(
                    data.Safety.MinSendIntervalMs,
                    data.Safety.MaxSendIntervalMs,
                    data.Safety.MaxMessagesPerDay,
                    data.Safety.MaxMessagesPerRecipientHour,
                    data.Safety.PendingRequests,
                    data.Safety.Day,
                    data.Safety.AttemptedToday,
                    data.Safety.SentToday,
                    data.Safety.UnknownToday),
            ConfiguredWhatsAppSessionManager.ComplianceNotice);

    private static WhatsAppSessionInfo Unavailable(string error)
        => new(
            WhatsAppProviders.LinkedDevice,
            "unavailable",
            IsAvailable: false,
            IsConnected: false,
            HasSavedSession: false,
            ConnectedPhone: null,
            QrDataUrl: null,
            QrExpiresAtUtc: null,
            LastError: error,
            ReconnectAttempt: 0,
            Safety: null,
            ConfiguredWhatsAppSessionManager.ComplianceNotice);

    private sealed class GatewayEnvelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public GatewayErrorData? Error { get; set; }
    }

    private sealed class GatewayErrorData
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public bool Retryable { get; set; }
        public int? RetryAfterSeconds { get; set; }
    }

    private sealed class GatewaySendData
    {
        public string? ProviderMessageId { get; set; }
        public bool Duplicate { get; set; }
        public DateTime? AcceptedAtUtc { get; set; }
    }

    private sealed class GatewaySessionData
    {
        public string? Provider { get; set; }
        public string? Status { get; set; }
        public bool IsConnected { get; set; }
        public bool HasSavedSession { get; set; }
        public string? ConnectedPhone { get; set; }
        public string? QrDataUrl { get; set; }
        public DateTime? QrExpiresAtUtc { get; set; }
        public string? LastError { get; set; }
        public int ReconnectAttempt { get; set; }
        public GatewaySafetyData? Safety { get; set; }
    }

    private sealed class GatewaySafetyData
    {
        public int MinSendIntervalMs { get; set; }
        public int MaxSendIntervalMs { get; set; }
        public int MaxMessagesPerDay { get; set; }
        public int MaxMessagesPerRecipientHour { get; set; }
        public int PendingRequests { get; set; }
        public string? Day { get; set; }
        public int AttemptedToday { get; set; }
        public int SentToday { get; set; }
        public int UnknownToday { get; set; }
    }
}
