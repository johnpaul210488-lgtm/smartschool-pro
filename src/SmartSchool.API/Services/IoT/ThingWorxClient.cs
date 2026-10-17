using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartSchool.API.Services.IoT;

/// <summary>Bus ek portal-entity ka snapshot.</summary>
public sealed record PortalBusLocation(double Latitude, double Longitude, double? SpeedKmh, DateTime TimestampUtc);

/// <summary>
/// ThingWorx REST client (self-discovering):
///  - Basic Auth ya appKey
///  - /Thingworx/Things list + per-Thing location property identify (auto)
///  - Discovery endpooint ke liye pehli cheez ka raw snippet rakhti hai (remotely debuggable)
/// </summary>
public sealed class ThingWorxClient
{
    // Location property candidates — ThingWorx deployments vary; first match wins.
    private static readonly string[] LocationProps = ["location", "Location", "GPSLocation", "gps", "gpslocation", "LatLng", "latlng", "Coordinates", "coordinates"];

    private readonly ThingWorxOptions _o;
    private readonly ILogger<ThingWorxClient> _logger;
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Last raw discovery output (IotController /api/iot/test isi ko dikhata hai).</summary>
    public string LastDiscoveryReport { get; private set; } = "(abhi discovery chali nahi — relay kuch seconds mein karegi)";

    public ThingWorxClient(IHttpClientFactory factory, IOptions<ThingWorxOptions> options, ILogger<ThingWorxClient> logger)
    {
        _o = options.Value;
        _logger = logger;
        _http = factory.CreateClient(nameof(ThingWorxClient));
        _http.BaseAddress = new Uri(_o.BaseUrl.TrimEnd('/') + "/Thingworx/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_o.AppKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("appKey", _o.AppKey);
        else if (!string.IsNullOrWhiteSpace(_o.Username))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_o.Username}:{_o.Password}")));
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _o.RequestTimeoutSeconds));
    }

    /// <summary>Portal se saari Things ke naam (or "# ERROR # ..." per failure).</summary>
    public async Task<List<string>> ListThingNamesAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await GetStringAsync("Things?pageSize=200", ct));
        var rows = new List<string>();
        if (doc.RootElement.TryGetProperty("rows", out var r))
            foreach (var it in r.EnumerateArray())
                if (it.TryGetProperty("name", out var n)) rows.Add(n.GetString() ?? "");
        return rows;
    }

    /// <summary>Ek Thing ka latest location — property auto-detect + timestamp sanity.</summary>
    public async Task<PortalBusLocation?> GetLocationAsync(string thing, CancellationToken ct)
    {
        foreach (var prop in LocationProps)
        {
            try
            {
                using var doc = JsonDocument.Parse(await GetStringAsync($"Things/{Uri.EscapeDataString(thing)}/Properties/{prop}", ct));
                var el = doc.RootElement;
                // Shape 1: { "value": { "latitude": x, "longitude": y }, "time": ms }
                DateTime tsUtc = DateTime.UtcNow; double? speedKmh = null;
                if (el.TryGetProperty("value", out var v)) el = v;
                if (TryGetCoord(el, out var lat, out var lng))
                {
                    foreach (var sp in _o.SpeedPropertyCandidates)
                    {
                        try
                        {
                            using var sdoc = JsonDocument.Parse(await GetStringAsync($"Things/{Uri.EscapeDataString(thing)}/Properties/{sp}", ct));
                            var sv = sdoc.RootElement;
                            if (sv.TryGetProperty("value", out var svv)) sv = svv;
                            if (sv.ValueKind == JsonValueKind.Number && sv.TryGetDouble(out var s2)) speedKmh = s2;
                            break;
                        }
                        catch { /* optional */ }
                    }
                    return new PortalBusLocation(lat, lng, speedKmh, tsUtc);
                }
            }
            catch (HttpRequestException) { /* property nahi hai next try */ }
        }
        return null;
    }

    private static bool TryGetCoord(JsonElement el, out double lat, out double lng)
    {
        lat = lng = 0;
        foreach (var (k1, k2) in new[] { ("latitude", "longitude"), ("lat", "lng"), ("lat", "lon") })
        {
            if (el.TryGetProperty(k1, out var a) && a.ValueKind == JsonValueKind.Number &&
                el.TryGetProperty(k2, out var b) && b.ValueKind == JsonValueKind.Number)
            {
                lat = a.GetDouble(); lng = b.GetDouble();
                return lat is > -90 and < 90 && lng is > -180 and < 180 && (lat != 0 || lng != 0);
            }
        }
        return false;
    }

    /// <summary>Portal ka catalog + first location sample — /api/iot/test endpoint is se map karta hai.</summary>
    public async Task<string> DiscoverAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        try
        {
            var names = await ListThingNamesAsync(ct);
            sb.AppendLine($"THINGS ({names.Count}) mil gayeen: {string.Join(", ", names.Take(60))}");
            var locCount = 0;
            foreach (var t in names)
            {
                var loc = await GetLocationAsync(t, ct);
                if (loc is null) continue;
                locCount++;
                sb.AppendLine($"LIVE   {t}:  {loc.Latitude}, {loc.Longitude} (speed {loc.SpeedKmh?.ToString("0") ?? "-"} km/h)");
            }
            sb.AppendLine(locCount == 0
                ? "!! Kisi Thing par location property nahi mili — portal par properties: manual check (ya demo Things hi hain)"
                : $"== {locCount} live entities hain — inke + Buses.Id mapping BusMap/DigitsLogic se ban jayegi ==");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"#ERROR# {ex.GetType().Name}: {ex.Message}");
        }
        LastDiscoveryReport = sb.ToString();
        _logger.LogInformation("ThingWorx Discovery:\n{R}", LastDiscoveryReport.Replace("\n", " | "));
        return LastDiscoveryReport;
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }
}
