namespace SmartSchool.Shared.Helpers;

/// <summary>
/// Normalizes Pakistani phone numbers to E.164 for Twilio.
/// Accepts: 03001234567, 3001234567, +923001234567, 923001234567.
/// Returns null when the number can't be salvaged.
/// </summary>
public static class PhoneNormalizer
{
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("00"))
            digits = digits[2..];

        // PK local mobile: 11 digits starting 0 -> +92...
        if (digits.Length == 11 && digits.StartsWith("0"))
            digits = "92" + digits[1..];
        // PK mobile without trunk zero: 10 digits starting 3 -> +923...
        else if (digits.Length == 10 && digits.StartsWith("3"))
            digits = "92" + digits;

        if (digits.Length is < 10 or > 15)
            return null;

        return "+" + digits;
    }

    public static string? ToWhatsAppAddress(string? raw)
        => ToE164(raw) is { } e164 ? $"whatsapp:{e164}" : null;

    public static bool IsValid(string? raw) => ToE164(raw) is not null;
}
