using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;

namespace SmartSchool.API.Services;

/// <summary>
/// Bug-report ka shared logic: DB row + dedupe (signature) + GitHub issue.
/// Controller bhi isi se use karta hai, background worker bhi (pending retry).
/// </summary>
public static class ErrorReportOutbox
{
    public static async Task<ErrorReport> SaveAsync(
        SchoolDbContext db,
        GitHubIssueService github,
        ErrorReportCreateRequest request,
        string? callerUserName,
        ILogger? logger,
        CancellationToken ct)
    {
        var title = (request.Title ?? "").Trim();
        var message = (request.Message ?? "").Trim();
        if (title.Length == 0) title = message.Length > 60 ? message[..60] : "(koi title nahi)";
        if (title.Length > 300) title = title[..300];

        var signature = ComputeSignature(request.Source ?? "Admin", title, request.StackTrace);
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-30);

        var existing = await db.ErrorReports
            .Where(e => e.SignatureHash == signature && !e.IsResolved && e.CreatedAtUtc > cutoff)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.Occurrences++;
            existing.LastSeenAtUtc = now;
            await db.SaveChangesAsync(ct);

            // agar pehli martaba GitHub nahi gai thi, ab le jao (with occurrences count)
            if (existing.GitHubIssueNumber is null)
                await TryPostIssueAsync(db, github, existing, callerUserName, logger, ct);
            return existing;
        }

        var row = new ErrorReport
        {
            Source = (request.Source ?? "Admin")[..Math.Min(20, (request.Source ?? "Admin").Length)],
            Severity = (request.Severity ?? "Error")[..Math.Min(10, (request.Severity ?? "Error").Length)],
            Title = title,
            Message = message,
            StackTrace = request.StackTrace is { Length: > 8000 } s ? s[..8000] : request.StackTrace,
            ContextJson = request.ContextJson is { Length: > 2000 } c ? c[..2000] : request.ContextJson,
            SignatureHash = signature,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };
        db.ErrorReports.Add(row);
        await db.SaveChangesAsync(ct);

        await TryPostIssueAsync(db, github, row, callerUserName, logger, ct);
        return row;
    }

    /// <summary>Pending (GitHubIssueNumber==null) reports ko GitHub ko bhejta — worker calls this.</summary>
    public static async Task TryFlushPendingAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<SchoolDbContext>();
        var github = services.GetRequiredService<GitHubIssueService>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("ErrorReportOutbox");
        if (!github.IsEnabled || !github.IsConfigured) return;

        var pending = await db.ErrorReports
            .Where(e => e.GitHubIssueNumber == null && !e.IsResolved && e.CreatedAtUtc > DateTime.UtcNow.AddDays(-7))
            .OrderBy(e => e.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        foreach (var row in pending)
            await TryPostIssueAsync(db, github, row, null, logger, ct);
    }

    private static async Task TryPostIssueAsync(
        SchoolDbContext db, GitHubIssueService github, ErrorReport row,
        string? callerUserName, ILogger? logger, CancellationToken ct)
    {
        // GitHub labels list must exist on the repo; unknown labels are auto-created by API when Issues:write.
        var labels = row.Source.ToLowerInvariant() is "parent" or "driver"
            ? new[] { "bug", "autoreport", "parent-app" }
            : new[] { "bug", "autoreport" };

        var issueTitle = $"[{row.Source}] {TrimTo(row.Title, 200)}";
        var body = BuildIssueBody(row, callerUserName);
        var number = await github.CreateIssueAsync(issueTitle, body, labels, ct);
        if (number is not null)
        {
            row.GitHubIssueNumber = number;
            await db.SaveChangesAsync(ct);
        }
    }

    public static string BuildIssueBody(ErrorReport row, string? callerUserName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("> 🤖 Yeh issue SmartSchool system ne khud ba-khud banayi hai.");
        sb.AppendLine();
        sb.AppendLine($"**Kahan se aayi:** {row.Source} app");
        sb.AppendLine($"**Severity:** {row.Severity}  •  **Dobara aaya:** {row.Occurrences}x");
        if (!string.IsNullOrWhiteSpace(callerUserName))
            sb.AppendLine($"**User:** {callerUserName}");
        sb.AppendLine($"**Waqt (Pakistan):** {SmartSchool.Shared.Helpers.PkTime.ToPk(row.CreatedAtUtc):dd-MMM-yyyy hh:mm tt}");
        sb.AppendLine();
        sb.AppendLine("### Masla");
        sb.AppendLine(string.IsNullOrWhiteSpace(row.Message) ? "(koi message nahi)" : TrimTo(row.Message, 1500));
        if (!string.IsNullOrWhiteSpace(row.ContextJson))
        {
            sb.AppendLine();
            sb.AppendLine("### Context");
            sb.AppendLine("```json");
            sb.AppendLine(TrimTo(row.ContextJson!, 1500));
            sb.AppendLine("```");
        }
        if (!string.IsNullOrWhiteSpace(row.StackTrace))
        {
            sb.AppendLine();
            sb.AppendLine("<details><summary>Stack trace</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(TrimTo(row.StackTrace!, 6000));
            sb.AppendLine("```");
            sb.AppendLine("</details>");
        }
        return sb.ToString();
    }

    public static string ComputeSignature(string source, string title, string? stackTrace)
    {
        var top = "";
        if (!string.IsNullOrWhiteSpace(stackTrace))
            foreach (var line in stackTrace.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("at ", StringComparison.Ordinal) || t.StartsWith("---", StringComparison.Ordinal))
                { top = t; break; }
            }
        var raw = $"{source}|{title}|{top}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..64];
    }

    private static string TrimTo(string s, int n) => s.Length <= n ? s : s[..n];
}
