using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Interfaces;
using SmartSchool.Core.Models;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TeachersController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public TeachersController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<PagedResult<TeacherListItem>>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.Teachers.Query()
            .Include(t => t.Classes)
            .Include(t => t.User)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.FullName, pattern) ||
                EF.Functions.Like(t.Phone, pattern) ||
                (t.Subject != null && EF.Functions.Like(t.Subject, pattern)));
        }

        var total = await query.CountAsync(ct);
        var pageItems = await query
            .OrderBy(t => t.FullName).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = pageItems.Select(t => new TeacherListItem(
            t.Id, t.FullName, t.Phone, t.Subject, t.UserId, t.User?.Username, t.IsActive,
            t.Classes.Select(c => $"{c.Name}-{c.Section}").ToList())).ToList();

        return OkEnvelope(PagedResult<TeacherListItem>.Create(items, page, pageSize, total));
    }

    /// <summary>Create a teacher profile; optionally creates a Teacher login in the same call.</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<TeacherListItem>>> Create(TeacherCreateRequest request, CancellationToken ct)
    {
        User? user = null;
        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var username = request.Username.Trim();
            if (await _uow.Users.AnyAsync(u => u.Username == username, ct))
                return ConflictEnvelope($"Username '{username}' is already taken.");

            user = new User
            {
                Username = username,
                FullName = request.FullName.Trim(),
                Role = UserRole.Teacher,
                PasswordHash = PasswordHasher.Hash(request.Password!),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            await _uow.Users.AddAsync(user, ct);
        }

        var teacher = new Teacher
        {
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            Subject = request.Subject?.Trim(),
            User = user,
            IsActive = true
        };
        await _uow.Teachers.AddAsync(teacher, ct);
        await _uow.SaveChangesAsync(ct);

        return CreatedEnvelope(new TeacherListItem(
            teacher.Id, teacher.FullName, teacher.Phone, teacher.Subject,
            teacher.UserId, user?.Username, teacher.IsActive, new List<string>()),
            "Teacher created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, TeacherUpdateRequest request, CancellationToken ct)
    {
        var teacher = await _uow.Teachers.FindAsync(new object[] { id }, ct);
        if (teacher is null)
            return NotFoundEnvelope($"Teacher {id} was not found.");

        teacher.FullName = request.FullName.Trim();
        teacher.Phone = request.Phone.Trim();
        teacher.Subject = request.Subject?.Trim();
        teacher.IsActive = request.IsActive;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Teacher updated successfully.");
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Deactivate(int id, CancellationToken ct)
    {
        var teacher = await _uow.Teachers.FindAsync(new object[] { id }, ct);
        if (teacher is null)
            return NotFoundEnvelope($"Teacher {id} was not found.");

        teacher.IsActive = false;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Teacher deactivated successfully.");
    }
}
