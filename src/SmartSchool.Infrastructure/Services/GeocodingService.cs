using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartSchool.Core.Interfaces;

namespace SmartSchool.Infrastructure.Services;

/// <summary>
/// Google Maps reverse geocoding. Without an API key (or on failure) returns a
/// "GPS 33.58690, 71.44140" label so the WhatsApp pipeline never breaks.
/// </summary>
public sealed class GoogleMapsGeocodingService : IGeocodingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleMapsGeocodingService> _logger;

    public GoogleMapsGeocodingService(HttpClient http, IConfiguration config, ILogger<GoogleMapsGeocodingService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<string> GetLocationLabelAsync(double lat, double lng, CancellationToken ct = default)
    {
        var fallback = string.Create(CultureInfo.InvariantCulture, $"GPS {lat:F5}, {lng:F5}");

        var apiKey = _config["GoogleMaps:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return fallback;

        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://maps.googleapis.com/maps/api/geocode/json?latlng={lat},{lng}&key={apiKey}");

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return fallback;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0)
                return fallback;

            return results[0].GetProperty("formatted_address").GetString() ?? fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reverse geocoding failed for {Lat},{Lng}", lat, lng);
            return fallback;
        }
    }
}
