namespace SmartSchool.Core.Models;

/// <summary>Explicit permission recorded by office staff for this student and number.</summary>
public sealed class WhatsAppConsent
{
    public long Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public string PhoneE164 { get; set; } = string.Empty;
    public bool Granted { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public int ChangedByUserId { get; set; }
}
