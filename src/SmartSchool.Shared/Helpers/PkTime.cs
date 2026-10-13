namespace SmartSchool.Shared.Helpers;

/// <summary>
/// Pakistan Standard Time (UTC+5, no daylight-saving).
/// DATABASE stores UTC; API wire/UI always speaks PST. Windows zone id:
/// "Pakistan Standard Time", Linux/macOS: "Asia/Karachi".
/// </summary>
public static class PkTime
{
    public static readonly TimeZoneInfo Tz = ResolveTz();

    private static TimeZoneInfo ResolveTz()
        => OperatingSystem.IsWindows()
            ? TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");

    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Current wall-clock time in Pakistan.</summary>
    public static DateTime NowPk => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    /// <summary>Today's date in Pakistan (drives "today's attendance" logic).</summary>
    public static DateOnly TodayPk => DateOnly.FromDateTime(NowPk);

    /// <summary>UTC instant (any Kind - unspecified treated as UTC) to PST wall time.</summary>
    public static DateTime ToPk(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Tz);
    }

    /// <summary>Start of a PST calendar day expressed in UTC (DB range queries).</summary>
    public static DateTime PkDayStartUtc(DateOnly pkDate)
        => TimeZoneInfo.ConvertTimeToUtc(pkDate.ToDateTime(TimeOnly.MinValue), Tz);

    /// <summary>Exclusive end of a PST calendar day expressed in UTC.</summary>
    public static DateTime PkDayEndUtc(DateOnly pkDate)
        => TimeZoneInfo.ConvertTimeToUtc(pkDate.AddDays(1).ToDateTime(TimeOnly.MinValue), Tz);
}
