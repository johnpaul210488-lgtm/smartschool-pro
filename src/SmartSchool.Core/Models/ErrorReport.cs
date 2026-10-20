namespace SmartSchool.Core.Models;

/// <summary>
/// Auto/manual bug report — Admin app, parent/driver apps, ya API khud bhejti hai.
/// Server isse DB mein rakhta hai aur (agar GitHub config on ho) GitHub Issue bhi bana deta hai.
/// Signature-hash se dobari reports pe Occurrences barhta hai — duplicate issues nahi banti.
/// </summary>
public sealed class ErrorReport
{
    public int Id { get; set; }

    /// <summary>"Admin" | "API" | "Parent" | "Driver"</summary>
    public string Source { get; set; } = string.Empty;

    public string Severity { get; set; } = "Error"; // Info / Warning / Error / Crash

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    /// <summary>JSON: app version, user, screen/page, extra notes.</summary>
    public string? ContextJson { get; set; }

    /// <summary>SHA256(Source + exception type + stack top lines) — dedupe ke liye.</summary>
    public string SignatureHash { get; set; } = string.Empty;

    public int Occurrences { get; set; } = 1;

    /// <summary>GitHub par banayi gai issue ka number (null = abhi GitHub ko nahi gayi).</summary>
    public int? GitHubIssueNumber { get; set; }

    public bool IsResolved { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
