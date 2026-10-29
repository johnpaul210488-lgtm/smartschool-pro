using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmartSchool.API.Services;

/// <summary>
/// Supervises a disabled-by-default, local-only QR TEST process. This service
/// deliberately does not implement IWhatsAppSender and never reads the outbox.
/// </summary>
public sealed class QrTestGatewayService : BackgroundService
{
    private readonly ILogger<QrTestGatewayService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(35)
    };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly string _gatewayDir = Path.Combine(AppContext.BaseDirectory, "whatsapp-gateway");
    private readonly string _stateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartSchool", "whatsapp-qr-test");
    private Process? _process;
    private string? _secret;
    private Uri? _baseUri;

    public QrTestGatewayService(ILogger<QrTestGatewayService> logger) => _logger = logger;
    private string Marker => Path.Combine(_stateDir, "enabled-test-only");

    // Restart a previously enabled process if the API or gateway restarted.
    // The node process will reconnect only registered sessions, never request a
    // fresh QR on its own. The service is inert until an admin starts a test.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (File.Exists(Marker))
            {
                try
                {
                    await _gate.WaitAsync(stoppingToken);
                    try { if (File.Exists(Marker)) await EnsureProcessAsync(stoppingToken); }
                    finally { _gate.Release(); }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { LogSafe(ex); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<QrGatewayReply> StatusAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(Marker)) return new(true, State: "disabled", TestOnly: true);
            await EnsureProcessAsync(ct);
            return await CallAsync(HttpMethod.Get, "status", null, ct);
        }
        catch (Exception ex) { LogSafe(ex); return Unavailable(); }
        finally { _gate.Release(); }
    }

    public async Task<QrGatewayReply> QrAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(Marker)) return new(true, QrDataUrl: null);
            await EnsureProcessAsync(ct);
            return await CallAsync(HttpMethod.Get, "qr", null, ct);
        }
        catch (Exception ex) { LogSafe(ex); return Unavailable(); }
        finally { _gate.Release(); }
    }

    public async Task<QrGatewayReply> StartAsync(bool riskConfirmed, CancellationToken ct)
    {
        if (!riskConfirmed) return new(false, Error: "Confirm the unofficial test-account risk first.");
        await _gate.WaitAsync(ct);
        var wasEnabled = File.Exists(Marker);
        try
        {
            await EnsureProcessAsync(ct);
            Directory.CreateDirectory(_stateDir);
            // The marker contains no phone, QR, key or credentials.
            await File.WriteAllTextAsync(Marker, "test-only", ct);
            var reply = await CallAsync(HttpMethod.Post, "session/start", new { riskConfirmed = true }, ct);
            if (!reply.Ok && !wasEnabled) { File.Delete(Marker); StopChild(); }
            return reply;
        }
        catch (Exception ex)
        {
            if (!wasEnabled)
            {
                try { File.Delete(Marker); } catch { /* reported below */ }
                StopChild();
            }
            LogSafe(ex);
            return Unavailable();
        }
        finally { _gate.Release(); }
    }

    public async Task<QrGatewayReply> SendAsync(string phone, string type, bool consentConfirmed, Guid requestId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(Marker)) return new(false, Result: "not_sent", Error: "The QR test is not enabled.");
            try { await EnsureProcessAsync(ct); }
            catch (Exception ex) { LogSafe(ex); return new(false, Result: "not_sent", Error: "QR test service is unavailable."); }
            try
            {
                return await CallAsync(HttpMethod.Post, "test/send",
                    new { phone, type, consentConfirmed, requestId = requestId.ToString("D") }, ct);
            }
            catch (Exception ex)
            {
                LogSafe(ex);
                // The request may have reached WhatsApp before the connection was
                // lost. Never retry automatically or claim it was not sent.
                return new(false, Result: "unknown", Error: "Outcome unknown. Check the test phone before trying again.");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<QrGatewayReply> UnlinkAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Disable auto-start first, even if WhatsApp is offline. The user is
            // told to remove the linked device on the phone if remote logout fails.
            try { File.Delete(Marker); }
            catch (Exception ex) { LogSafe(ex); return new(false, Error: "Could not disable the test session."); }
            var phoneUnlinked = false;
            try
            {
                if (_process is not null && !_process.HasExited)
                {
                    var reply = await CallAsync(HttpMethod.Post, "session/unlink", new { confirm = true }, ct);
                    phoneUnlinked = reply.Ok && reply.PhoneUnlinked == true;
                }
            }
            catch (Exception ex) { LogSafe(ex); }
            StopChild();
            var auth = Path.Combine(_stateDir, "auth");
            if (Directory.Exists(auth))
            {
                if ((File.GetAttributes(auth) & FileAttributes.ReparsePoint) != 0)
                    return new(false, Error: "Unsafe local session directory; remove linked device on the phone.");
                Directory.Delete(auth, recursive: true);
            }
            return new(true, State: "disabled", PhoneUnlinked: phoneUnlinked);
        }
        catch (Exception ex) { LogSafe(ex); return new(false, Error: "Could not remove the local session; remove linked device on the phone."); }
        finally { _gate.Release(); }
    }

    private async Task EnsureProcessAsync(CancellationToken ct)
    {
        if (_process is not null && !_process.HasExited && _baseUri is not null && _secret is not null) return;
        StopChild();
        var node = Path.Combine(_gatewayDir, OperatingSystem.IsWindows() ? "node.exe" : "node");
        var entry = Path.Combine(_gatewayDir, "src", "index.js");
        if (!File.Exists(node) || !File.Exists(entry)) throw new InvalidOperationException("QR test components are not installed.");
        Directory.CreateDirectory(_stateDir);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var info = new ProcessStartInfo(node)
        {
            WorkingDirectory = _gatewayDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(entry);
        info.Environment["SMARTSCHOOL_QR_SECRET"] = secret;
        info.Environment["SMARTSCHOOL_QR_STATE_DIR"] = _stateDir;
        info.Environment["SMARTSCHOOL_QR_PARENT_PID"] = Environment.ProcessId.ToString();
        var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start QR test component.");
        _process = child;
        _ = DrainAsync(child.StandardError);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var ready = await child.StandardOutput.ReadLineAsync(timeout.Token);
            if (ready is null || !ready.StartsWith("READY ", StringComparison.Ordinal) ||
                !int.TryParse(ready.AsSpan(6), out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("QR test component did not become ready.");
            _ = DrainAsync(child.StandardOutput);
            _baseUri = new Uri($"http://127.0.0.1:{port}/");
            _secret = secret;
        }
        catch { StopChild(); throw; }
    }

    private async Task<QrGatewayReply> CallAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri!, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        if (body is not null) request.Content = JsonContent.Create(body, options: _json);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("QR test component rejected the request.");
        return await response.Content.ReadFromJsonAsync<QrGatewayReply>(_json, ct)
            ?? throw new HttpRequestException("QR test component returned an empty reply.");
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try { while (await reader.ReadLineAsync() is not null) { /* discard; never log QR/session data */ } }
        catch { /* child exited */ }
    }

    private void StopChild()
    {
        var child = _process;
        _process = null;
        _secret = null;
        _baseUri = null;
        if (child is null) return;
        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(5000);
            }
        }
        catch { /* already exited */ }
        finally { child.Dispose(); }
    }

    private void LogSafe(Exception ex) => _logger.LogWarning("QR test service unavailable ({ErrorType})", ex.GetType().Name);
    private static QrGatewayReply Unavailable() => new(false, Error: "QR test service is unavailable. Check the test installer and try again.");

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { StopChild(); }
        finally { _gate.Release(); }
    }
}

public sealed record QrGatewayReply(
    bool Ok,
    string? State = null,
    bool? TestOnly = null,
    string? QrDataUrl = null,
    string? Result = null,
    string? Error = null,
    bool? PhoneUnlinked = null);
