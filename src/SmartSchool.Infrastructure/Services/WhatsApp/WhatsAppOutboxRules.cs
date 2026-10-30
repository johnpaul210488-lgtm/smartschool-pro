namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>Bus messages are time-sensitive; neither old events nor old routing epochs are replayed.</summary>
public static class WhatsAppOutboxRules
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    public static bool IsFresh(DateTime? activationUtc, DateTime eventUtc, DateTime nowUtc)
        => activationUtc is { } since && eventUtc >= since && eventUtc <= nowUtc.AddMinutes(1)
           && eventUtc >= nowUtc - MaxAge;
}
