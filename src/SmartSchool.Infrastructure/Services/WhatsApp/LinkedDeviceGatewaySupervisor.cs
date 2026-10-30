using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>
/// Starts and monitors the bundled, loopback-only Node gateway. It never starts
/// for DryRun/Twilio and never kills a gateway process it did not create.
/// </summary>
public sealed class LinkedDeviceGatewaySupervisor : BackgroundService
{
    private readonly WhatsAppOptions _options;
    private readonly ILogger<LinkedDeviceGatewaySupervisor> _logger;
    private readonly HttpClient _healthClient;
    private Process? _ownedProcess;
    private int _consecutiveStartFailures;
    private int _consecutiveUnhealthyChecks;

    public LinkedDeviceGatewaySupervisor(
        IOptions<WhatsAppOptions> options,
        ILogger<LinkedDeviceGatewaySupervisor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _healthClient = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var linked = _options.LinkedDevice;
        if (!_options.Provider.Equals(WhatsAppProviders.LinkedDevice, StringComparison.OrdinalIgnoreCase)
            || !linked.AutoStart)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var healthy = await IsHealthyAsync(linked.BaseUrl, stoppingToken);
                if (healthy)
                {
                    _consecutiveStartFailures = 0;
                    _consecutiveUnhealthyChecks = 0;
                }
                else if (_ownedProcess is null || _ownedProcess.HasExited)
                {
                    StartGateway(linked);
                }
                else if (++_consecutiveUnhealthyChecks >= 6)
                {
                    _logger.LogWarning(
                        "Owned linked-device gateway stayed unhealthy for {Checks} checks; restarting it.",
                        _consecutiveUnhealthyChecks);
                    _ownedProcess.Kill(entireProcessTree: true);
                    _consecutiveUnhealthyChecks = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveStartFailures++;
                _logger.LogError(ex, "Linked-device gateway supervision failed (attempt {Attempt}).",
                    _consecutiveStartFailures);
            }

            var delaySeconds = Math.Min(60, 5 * Math.Max(1, _consecutiveStartFailures));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var health = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "health");
            using var response = await _healthClient.GetAsync(health, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void StartGateway(LinkedDeviceOptions linked)
    {
        _consecutiveUnhealthyChecks = 0;
        _ownedProcess?.Dispose();
        _ownedProcess = null;

        var node = WhatsAppPathResolver.Resolve(linked.NodeExecutable);
        var entry = WhatsAppPathResolver.Resolve(linked.EntryPoint);
        if (!File.Exists(node) || !File.Exists(entry))
        {
            _consecutiveStartFailures++;
            _logger.LogWarning(
                "Linked-device provider is selected but gateway files are missing. Node={NodeExists}, Entry={EntryExists}. " +
                "Install the full school-server package or run the gateway manually.",
                File.Exists(node), File.Exists(entry));
            return;
        }

        var baseUri = new Uri(linked.BaseUrl);
        if (!baseUri.IsLoopback)
            throw new InvalidOperationException("The supervised WhatsApp gateway BaseUrl must be loopback-only.");

        var start = new ProcessStartInfo
        {
            FileName = node,
            WorkingDirectory = Directory.GetParent(Path.GetDirectoryName(entry)!)?.FullName
                               ?? Path.GetDirectoryName(entry)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(entry);
        start.Environment["SMARTSCHOOL_WHATSAPP_HOST"] = baseUri.Host;
        start.Environment["SMARTSCHOOL_WHATSAPP_PORT"] = baseUri.Port.ToString();

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            _consecutiveStartFailures++;
            _logger.LogWarning("Linked-device gateway exited with code {ExitCode}; supervisor will restart it.",
                SafeExitCode(process));
        };
        if (!process.Start()) throw new InvalidOperationException("Process.Start returned false for WhatsApp gateway.");
        _ownedProcess = process;
        _ = DrainAsync(process.StandardOutput);
        _ = DrainAsync(process.StandardError);
        _logger.LogInformation("Started the isolated linked-device WhatsApp gateway (PID {Pid}).", process.Id);
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is not null) { }
        }
        catch { /* child exit closes redirected streams */ }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return null; }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_ownedProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not stop the owned linked-device gateway process cleanly.");
            }
        }
        _ownedProcess?.Dispose();
        _healthClient.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
