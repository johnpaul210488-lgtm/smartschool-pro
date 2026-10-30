using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;

namespace SmartSchool.API.Controllers;

/// <summary>
/// Staff explicitly links a parent login to a child. Do not infer links from phone
/// numbers: a link also grants access to the child's data, not just push alerts.
/// </summary>
[ApiController]
[Authorize(Roles = AppRoles.Staff)]
[Route("api/parent-links")]
public sealed class ParentLinksController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public ParentLinksController(IUnitOfWork uow) => _uow = uow;

    /// <summary>List the linked accounts and their device counts, never the device tokens.</summary>
    [HttpGet("student/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> ForStudent(int studentId, CancellationToken ct)
    {
        if (!await _uow.Students.AnyAsync(s => s.Id == studentId, ct))
            return NotFoundEnvelope("Student not found.");

        var accounts = await _uow.ParentStudents.Query()
            .Where(link => link.StudentId == studentId)
            .Select(link => new { link.ParentUserId, link.ParentUser.Username, link.ParentUser.FullName, link.ParentUser.IsActive })
            .ToListAsync(ct);
        if (accounts.Count == 0)
            return OkEnvelope((object)new List<ParentLinkStatus>());
        var parentIds = accounts.Select(a => a.ParentUserId).ToArray();
        var counts = await _uow.DeviceTokens.Query()
            .Where(d => parentIds.Contains(d.UserId))
            .GroupBy(d => d.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var results = accounts.Select(a => new ParentLinkStatus(
            a.ParentUserId, a.Username, a.FullName, a.IsActive, counts.GetValueOrDefault(a.ParentUserId))).ToList();
        return OkEnvelope((object)results);
    }

    /// <summary>Idempotently link an active parent account to an active student.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> Link(LinkParentStudentRequest request, CancellationToken ct)
    {
        var username = request.ParentUsername?.Trim();
        if (string.IsNullOrEmpty(username) || request.StudentId <= 0)
            return BadRequestEnvelope("ParentUsername and a positive StudentId are required.");

        var parent = await _uow.Users.Query()
            .FirstOrDefaultAsync(u => u.Username == username && u.Role == UserRole.Parent && u.IsActive, ct);
        if (parent is null)
            return NotFoundEnvelope("Active parent account not found.");
        if (!await _uow.Students.AnyAsync(s => s.Id == request.StudentId && s.IsActive, ct))
            return NotFoundEnvelope("Active student not found.");

        var exists = await _uow.ParentStudents.AnyAsync(
            link => link.ParentUserId == parent.Id && link.StudentId == request.StudentId, ct);
        if (!exists)
        {
            await _uow.ParentStudents.AddAsync(new ParentStudent
            {
                ParentUserId = parent.Id,
                StudentId = request.StudentId
            }, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two staff requests can race on the composite primary key.
                // Treat it as already linked only if the row really exists.
                exists = await _uow.ParentStudents.AnyAsync(
                    link => link.ParentUserId == parent.Id && link.StudentId == request.StudentId, ct);
                if (!exists) throw;
            }
        }

        return OkEnvelope((object)new { parentUserId = parent.Id, studentId = request.StudentId, alreadyLinked = exists },
            exists ? "Parent is already linked." : "Parent linked. Restart the parent app to refresh its live subscriptions.");
    }
}

public sealed record ParentLinkStatus(int ParentUserId, string Username, string FullName, bool IsActive, int RegisteredDevices);

public sealed class LinkParentStudentRequest
{
    public string? ParentUsername { get; set; }
    public int StudentId { get; set; }
}
