using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSchool.Core.Interfaces;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

public sealed class DryRunWhatsAppSender
{
    private readonly ILogger<DryRunWhatsAppSender> _logger;

    public DryRunWhatsAppSender(ILogger<DryRunWhatsAppSender> logger) => _logger = logger;

    public Task<WhatsAppSendResult> SendAsync(
        Guid correlationId,
        string toPhone,
        string body,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = PhoneNormalizer.ToE164(toPhone);
        if (normalized is null)
            return Task.FromResult(WhatsAppSendResult.Fail(
                $"Invalid phone number '{toPhone}'.",
                WhatsAppFailureKind.Permanent,
                retryable: false));

        // Do not put full message bodies or phone numbers into operational logs.
        _logger.LogInformation(
            "[WhatsApp DRY-RUN] Correlation={CorrelationId}; recipient ending {PhoneSuffix}; chars={Length}",
            correlationId, normalized[^4..], body.Length);
        return Task.FromResult(WhatsAppSendResult.Ok($"DRYRUN-{correlationId:N}"));
    }
}

/// <summary>One provider selector; controllers and the durable outbox never contain provider-specific branches.</summary>
public sealed class ConfiguredWhatsAppSender : IWhatsAppSender
{
    private readonly WhatsAppOptions _options;
    private readonly DryRunWhatsAppSender _dryRun;
    private readonly TwilioWhatsAppSender _twilio;
    private readonly LinkedDeviceWhatsAppClient _linkedDevice;

    public ConfiguredWhatsAppSender(
        IOptions<WhatsAppOptions> options,
        DryRunWhatsAppSender dryRun,
        TwilioWhatsAppSender twilio,
        LinkedDeviceWhatsAppClient linkedDevice)
    {
        _options = options.Value;
        _dryRun = dryRun;
        _twilio = twilio;
        _linkedDevice = linkedDevice;
    }

    public Task<WhatsAppSendResult> SendAsync(
        Guid correlationId,
        string toPhone,
        string body,
        CancellationToken ct = default)
        => (_options.Provider ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dryrun" => _dryRun.SendAsync(correlationId, toPhone, body, ct),
            "twilio" => _twilio.SendAsync(correlationId, toPhone, body, ct),
            "linkeddevice" => _linkedDevice.SendAsync(correlationId, toPhone, body, ct),
            _ => Task.FromResult(WhatsAppSendResult.Fail(
                $"Unknown WhatsApp provider '{_options.Provider}'. Use DryRun, Twilio or LinkedDevice.",
                WhatsAppFailureKind.Configuration,
                retryable: false))
        };
}

public sealed class ConfiguredWhatsAppSessionManager : IWhatsAppSessionManager
{
    public const string ComplianceNotice =
        "Linked-device mode uses an unofficial WhatsApp Web client and cannot guarantee against account restrictions. " +
        "Use only for consented, transactional parent alerts; do not send bulk/unsolicited marketing, scrape contacts, " +
        "or automate groups. WhatsApp alone controls account suspension and review.";

    private readonly WhatsAppOptions _options;
    private readonly LinkedDeviceWhatsAppClient _linkedDevice;
    private readonly IConfiguration _configuration;

    public ConfiguredWhatsAppSessionManager(
        IOptions<WhatsAppOptions> options,
        LinkedDeviceWhatsAppClient linkedDevice,
        IConfiguration configuration)
    {
        _options = options.Value;
        _linkedDevice = linkedDevice;
        _configuration = configuration;
    }

    public Task<WhatsAppSessionInfo> GetStatusAsync(CancellationToken ct = default)
    {
        if (IsLinkedDevice()) return _linkedDevice.GetStatusAsync(ct);
        var provider = string.IsNullOrWhiteSpace(_options.Provider) ? WhatsAppProviders.DryRun : _options.Provider;
        var isDryRun = provider.Equals(WhatsAppProviders.DryRun, StringComparison.OrdinalIgnoreCase);
        var isTwilio = provider.Equals(WhatsAppProviders.Twilio, StringComparison.OrdinalIgnoreCase);
        var twilioFrom = _configuration["Twilio:WhatsAppFrom"]
                         ?? _configuration["Twilio:FromWhatsAppNumber"];
        var twilioReady = !string.IsNullOrWhiteSpace(_configuration["Twilio:AccountSid"])
                          && !string.IsNullOrWhiteSpace(_configuration["Twilio:AuthToken"])
                          && !string.IsNullOrWhiteSpace(twilioFrom);
        var knownProvider = isDryRun || isTwilio;
        var isReady = isDryRun || (isTwilio && twilioReady);
        var configurationError = !knownProvider
            ? $"Unknown WhatsApp provider '{provider}'."
            : "Twilio is selected but its credentials/sender are incomplete.";
        return Task.FromResult(new WhatsAppSessionInfo(
            provider,
            isDryRun ? "dry_run" : isReady ? "managed_provider" : "configuration_error",
            IsAvailable: isReady,
            IsConnected: !isDryRun && isReady,
            HasSavedSession: false,
            ConnectedPhone: null,
            QrDataUrl: null,
            QrExpiresAtUtc: null,
            LastError: isReady ? null : configurationError,
            ReconnectAttempt: 0,
            Safety: null,
            ComplianceNotice));
    }

    public Task<WhatsAppSessionCommandResult> ConnectAsync(bool acceptTermsAndRisk, CancellationToken ct = default)
        => IsLinkedDevice()
            ? _linkedDevice.ConnectAsync(acceptTermsAndRisk, ct)
            : NotApplicable("QR linking is available only when WhatsApp:Provider is LinkedDevice.", ct);

    public Task<WhatsAppSessionCommandResult> LogoutAsync(bool confirmUnlink, CancellationToken ct = default)
        => IsLinkedDevice()
            ? _linkedDevice.LogoutAsync(confirmUnlink, ct)
            : NotApplicable("There is no linked-device session for the selected provider.", ct);

    private bool IsLinkedDevice()
        => string.Equals(_options.Provider, WhatsAppProviders.LinkedDevice, StringComparison.OrdinalIgnoreCase);

    private async Task<WhatsAppSessionCommandResult> NotApplicable(string error, CancellationToken ct)
    {
        var status = await GetStatusAsync(ct);
        return new WhatsAppSessionCommandResult(false, status, error);
    }
}
