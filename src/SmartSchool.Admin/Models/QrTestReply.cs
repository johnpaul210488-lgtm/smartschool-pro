namespace SmartSchool.Admin.Models;

// Response from the authenticated, test-only QR endpoints; no school queue data.
public sealed class QrTestReply
{
    public bool Ok { get; set; }
    public string? State { get; set; }
    public bool? TestOnly { get; set; }
    public string? QrDataUrl { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public bool? PhoneUnlinked { get; set; }
}
