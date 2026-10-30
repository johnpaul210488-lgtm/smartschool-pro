namespace SmartSchool.Infrastructure.Services.WhatsApp;

/// <summary>Bus alerts are time-sensitive; delayed/offline events are never replayed as current alerts.</summary>
public static class WhatsAppOutboxRules
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static bool IsFresh(DateTime eventUtc, DateTime nowUtc)
        => eventUtc <= nowUtc.AddMinutes(1) && eventUtc >= nowUtc - MaxAge;
}
