using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.API.Services;
using SmartSchool.Core.DTOs;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

/// <summary>
/// Har app (Admin/Parent/Driver) yahan bug report bhejti hai: DB + auto GitHub issue.
/// Staff list/manage kar sakti hai — dedupe via signature, retry via background worker.
/// </summary>
[ApiController]
[Route("api/errorreports")]
[Authorize]
public class ErrorReportsController(
    SchoolDbContext db,
    GitHubIssueService github,
    ILogger<ErrorReportsController> logger) : ControllerBase
{
    /// <summary>Koi bhi logged-in app user report kar sakta hai (admin, teacher, parent, driver).</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ErrorReportCreatedResponse>>> Create(
        ErrorReportCreateRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(ApiResponse.Fail("Empty request."));
        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(ApiResponse.Fail("Title ya message zaroori hai."));

        var userName = User?.Identity?.Name ?? User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var row = await ErrorReportOutbox.SaveAsync(db, github, request, userName, logger, ct);

        return Ok(ApiResponse<ErrorReportCreatedResponse>.Ok(
            new ErrorReportCreatedResponse(row.Id, row.GitHubIssueNumber, row.Occurrences),
            row.GitHubIssueNumber is not null
                ? $"Report mota ho gai — GitHub issue #{row.GitHubIssueNumber}"
                : "Report safe ho gai (GitHub abhi off/unreachable — baad mein khud bheji jayegi)"));
    }

    /// <summary>Staff: tamam reports jitni der say resolution nahi hui (latest 200).</summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<List<ErrorReportListItem>>>> List(CancellationToken ct)
    {
        var items = await db.ErrorReports
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var list = items.Select(e => new ErrorReportListItem(
            e.Id, e.Source, e.Severity,
            e.Title, TrimTo(e.Message, 200),
            e.Occurrences, e.GitHubIssueNumber, e.IsResolved,
            PkTime.ToPk(e.CreatedAtUtc).ToString("dd-MMM-yy hh:mm tt"),
            PkTime.ToPk(e.LastSeenAtUtc).ToString("dd-MMM-yy hh:mm tt"))).ToList();

        return Ok(ApiResponse<List<ErrorReportListItem>>.Ok(list));
    }

    /// <summary>Staff: report ko resolved/unresolved mark karein.</summary>
    [HttpPut("{id:int}/resolve")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<IActionResult> Resolve(int id, [FromBody] bool resolved, CancellationToken ct)
    {
        var row = await db.ErrorReports.FindAsync([id], ct);
        if (row is null) return NotFound();
        row.IsResolved = resolved;
        await db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    private static string TrimTo(string s, int n) => s.Length <= n ? s : s[..n];
}
