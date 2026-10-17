using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSchool.Core.DTOs;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Services.IoT;

/// <summary>
/// Relays live bus positions from the school's ThingWorx IoT portal into
/// SignalR: poll every PollSeconds → BusLocationUpdate → the same Admin live-
/// map the driver-app feeds. Runs only when ThingWorx:Enabled=true.
/// </summary>
public sealed class ThingWorxRelayService : BackgroundService
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThingWorxClient _twx;
    private readonly IOptions<ThingWorxOptions> _options;
    private readonly ILogger<ThingWorxRelayService> _logger;
    private bool _discoveredOnce;

    public ThingWorxRelayService(IServiceScopeFactory scopeFactory, ThingWorxClient twx,
        IOptions<ThingWorxOptions> options, ILogger<ThingWorxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _twx = twx;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = _options.Value;
        if (!o.Enabled)
        {
            _logger.LogInformation("ThingWorx relay DISABLED (ThingWorx:Enabled=false) — skipping.");
            return;
        }

        _logger.LogInformation("ThingWorx relay ON — {Url} (har {S}s)", o.BaseUrl, o.PollSeconds);

        // First-run discovery: portal ka asli structure logs mein likh do (aapki nazar baghair bhi debuggable).
        var discoverTimer = Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(
            async _ => { try { if (!_discoveredOnce) { _discoveredOnce = true; await _twx.DiscoverAsync(stoppingToken); } } catch { } },
            TaskContinuationOptions.OnlyOnRanToCompletion);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(o, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("ThingWorx relay poll fail: {Msg}", ex.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, o.PollSeconds)), stoppingToken);
        }
    }

    private async Task PollOnceAsync(ThingWorxOptions o, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var notify = scope.ServiceProvider.GetRequiredService<SmartSchool.Core.Interfaces.ISchoolNotificationService>();

        var thingNames = await _twx.ListThingNamesAsync(ct);
        if (o.Things.Length > 0)
            thingNames = thingNames.Where(t => o.Things.Any(f => t.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToList();

        var buses = await db.Buses.AsNoTracking().ToListAsync(ct); // few rows only

        foreach (var thing in thingNames)
        {
            var (busId, busNumber) = ResolveBus(o, thing, buses);
            if (busId <= 0) continue; // unmapped — discovery log already batata hai kya maujood hai

            var loc = await _twx.GetLocationAsync(thing, ct);
            if (loc is null)
            {
                _logger.LogDebug("ThingWorx '{T}' par location nahi", thing);
                continue;
            }

            await notify.SendBusLocationAsync(new BusLocationUpdate
            {
                BusId = busId,
                BusNumber = busNumber,
                DriverName = "IoT Portal",
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                SpeedKmh = loc.SpeedKmh,
                TimePakistan = PkTime.NowPk.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture)
            }, ct);
        }
    }

    /// <summary>Thing name → Bus. Config BusMap pehle, phir digits-match shardhana.</summary>
    private static (int BusId, string BusNumber) ResolveBus(ThingWorxOptions o, string thing, List<SmartSchool.Core.Models.Bus> buses)
    {
        var manual = o.BusMap.FirstOrDefault(m => m.Thing.Equals(thing, StringComparison.OrdinalIgnoreCase));
        if (manual is not null && manual.BusId > 0)
        {
            var b = buses.FirstOrDefault(x => x.Id == manual.BusId);
            if (b is not null) return (b.Id, b.BusNumber);
        }
        // digits heuristic: "QIS-Bus-01" → 1 → Bus.Number match 01/1
        var digits = Digits.Match(thing);
        if (digits.Success)
        {
            var num = digits.Value.TrimStart('0');
            var b = buses.FirstOrDefault(x =>
                x.BusNumber == digits.Value ||
                x.BusNumber.TrimStart('0') == num ||
                x.BusNumber.Equals($"B-{num}", StringComparison.OrdinalIgnoreCase));
            if (b is not null) return (b.Id, b.BusNumber);
        }
        return (0, "");
    }
}
