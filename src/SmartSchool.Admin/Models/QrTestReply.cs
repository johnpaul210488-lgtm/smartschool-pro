namespace SmartSchool.Admin.Models;

// Response from the authenticated QR endpoints.
public sealed class QrTestReply
{
    public bool Ok { get; set; }
    public string? State { get; set; }
    public bool? TestOnly { get; set; }
    public string? QrDataUrl { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public bool? PhoneUnlinked { get; set; }
    public bool? LiveEnabled { get; set; }
    public bool? LiveSessionMatches { get; set; }
}

public sealed class WhatsAppConsentStudent
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public List<WhatsAppConsentContact> Contacts { get; set; } = new();
}
public sealed class WhatsAppConsentContact
{
    public string Label { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Granted { get; set; }
    public bool ParentOptedOut { get; set; }
    public string Description => $"{Label}: {Phone} — " +
        (ParentOptedOut ? "parent opted out (blocked)" : Granted ? "permission recorded" : "permission not recorded");
}

public sealed class WhatsAppLogListItem
{
    public long Id { get; set; }
    public string? StudentName { get; set; }
    public string ParentPhone { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public string CreatedAtPakistan { get; set; } = "";
}
