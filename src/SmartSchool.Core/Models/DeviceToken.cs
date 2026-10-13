namespace SmartSchool.Core.Models;

/// <summary>FCM device registration for push notifications (Step 4 wiring).</summary>
public sealed class DeviceToken
{
    public long Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Token { get; set; } = string.Empty;

    /// <summary>android | ios</summary>
    public string Platform { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
