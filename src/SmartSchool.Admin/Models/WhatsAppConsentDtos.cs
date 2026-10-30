namespace SmartSchool.Admin.Models;

public sealed class WhatsAppConsentStudent
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public List<WhatsAppConsentContact> Contacts { get; set; } = new();
}

public sealed class WhatsAppConsentContact
{
    public string Label { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool Granted { get; set; }
    public bool ParentOptedOut { get; set; }

    public string Description => $"{Label}: {Phone} — " +
        (ParentOptedOut
            ? "parent opted out (blocked)"
            : Granted ? "permission recorded" : "permission not recorded");
}
