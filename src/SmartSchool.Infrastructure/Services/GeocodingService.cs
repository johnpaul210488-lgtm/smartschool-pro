using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;

namespace SmartSchool.Infrastructure.Services;

/// <summary>
/// OpenStreetMap Nominatim reverse geocoding (free, bina API key ke).
/// Coordinates ki jagah jagah ka naam deta hai (e.g. "Street 5, Kohat Cantt, Kohat").
/// Nominatim rules: 1 request/second, valid User-Agent. Isi liye throttle + cache lagaya gaya hai.
/// Failure par "GPS lat, lng" fallback deta hai taake WhatsApp/alert pipeline na ruke.
/// </summary>
public sealed class OsmGeocodingService : IGeocodingService
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCallUtc = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly ILogger<OsmGeocodingService> _logger;

    public OsmGeocodingService(HttpClient http, ILogger<OsmGeocodingService> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(4);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartSchoolPro", "1.0"));
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> GetLocationLabelAsync(double lat, double lng, CancellationToken ct = default)
    {
        var fallback = string.Create(CultureInfo.InvariantCulture, $"GPS {lat:F5}, {lng:F5}");

        // ~11 metre tak ek hi jagah: cache key rounded coordinates.
        var key = string.Create(CultureInfo.InvariantCulture, $"{lat:F4},{lng:F4}");
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Attendance request ko na roke: poora lookup (queue + network) 4 second mein khatam.
        // Late hone par GPS fallback label; attendance phir bhi save hoti hai.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        var token = budget.Token;

        try
        {
            await Gate.WaitAsync(token);
            try
            {
                var wait = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastCallUtc);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token);

                var url = string.Create(CultureInfo.InvariantCulture,
                    $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18&addressdetails=1&accept-language=en&lat={lat}&lon={lng}");
                using var response = await _http.GetAsync(url, token);
                _lastCallUtc = DateTime.UtcNow;
                if (!response.IsSuccessStatusCode) return fallback;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var label = BuildLabel(doc.RootElement, fallback);
                if (label != fallback) Cache[key] = label;
                return label;
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Budget khatam (slow/queue) - fallback, koi error nahi.
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reverse geocoding failed for {Lat},{Lng}", lat, lng);
            return fallback;
        }
    }

    private static string BuildLabel(JsonElement root, string fallback)
    {
        if (!root.TryGetProperty("address", out var addr) || addr.ValueKind != JsonValueKind.Object)
        {
            return root.TryGetProperty("display_name", out var dn) && dn.GetString() is { Length: > 0 } full
                ? Trim(full)
                : fallback;
        }

        string? Get(string name) =>
            addr.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var street = Get("road") ?? Get("neighbourhood") ?? Get("suburb");
        var area = Get("suburb") ?? Get("neighbourhood");
        var city = Get("city") ?? Get("town") ?? Get("village") ?? Get("county");

        var parts = new List<string>();
        foreach (var p in new[] { street, area, city })
        {
            if (!string.IsNullOrWhiteSpace(p) && !parts.Contains(p, StringComparer.OrdinalIgnoreCase))
                parts.Add(p.Trim());
        }

        if (parts.Count == 0)
            return root.TryGetProperty("display_name", out var dn2) && dn2.GetString() is { Length: > 0 } full2
                ? Trim(full2)
                : fallback;

        return Trim(string.Join(", ", parts));
    }

    private static string Trim(string s) => s.Length <= 280 ? s : s[..280];
}
