namespace SmartSchool.API.Services.IoT;

/// <summary>
/// ThingWorx (Etisalat IoT portal) integration config — appsettings.json → "ThingWorx".
/// NOTE: Enabled=false par service kaam nahi karti (safe default). Portal credentials
/// sirf INSTALLED appsettings.json mein rakhein — git mein kabhi nahi.
/// </summary>
public sealed class ThingWorxOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>e.g. https://iotmobility.etisalatdigital.ae  (no trailing slash)</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Portal username (Basic Auth). AppKey ho to use karna behtar hai.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>ThingWorx Application Key — set ho to Basic Auth ki jagah yeh use hota hai (recommended).</summary>
    public string AppKey { get; set; } = "";

    public int PollSeconds { get; set; } = 15;
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>Portal Thing names filter (optional): ["QIS-Bus-01", ...]. Khali → auto-discover location wali entities.</summary>
    public string[] Things { get; set; } = [];

    /// <summary>Manual mapping Thing name → hamari Buses.Id. Auto-match bhi karta hai (digits).</summary>
    public ThingToBusMap[] BusMap { get; set; } = [];

    /// <summary>Portal location values kitni stale hon ke skip ho (guard: purani frozen data na chadhe).</summary>
    public int MaxStaleMinutes { get; set; } = 30;

    /// <summary>Speed property name agar MileSpeed waghaira ho portal par (optional).</summary>
    public string[] SpeedPropertyCandidates { get; set; } = ["speed", "Speed", "SpeedKmh", "speedKmh", "velocity"];
}

public sealed class ThingToBusMap
{
    public string Thing { get; set; } = "";
    public int BusId { get; set; }
}
