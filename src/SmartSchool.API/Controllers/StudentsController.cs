using System.Globalization;
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
public sealed class StudentsController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public StudentsController(IUnitOfWork uow) => _uow = uow;

    private Task<bool> ParentOwnsStudentAsync(int studentId, CancellationToken ct)
        => _uow.ParentStudents.AnyAsync(
            ps => ps.ParentUserId == CallerUserId && ps.StudentId == studentId, ct);

    /// <summary>Student ki profile photo upload (jpg/png ≤8MB) → /uploads/students/{id}.jpg</summary>
    [HttpPost("{id:int}/photo")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Photo(int id, IFormFile file, CancellationToken ct)
    {
        var student = await _uow.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return NotFoundEnvelope("Student nahi mila.");
        if (file is null || file.Length == 0) return BadRequestEnvelope("Photo file chahiye.");
        if (file.Length > 8_000_000) return BadRequestEnvelope("Photo 8MB se bari nahi ho sakti.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png"))
            return BadRequestEnvelope("Sirf JPG ya PNG photos.");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SmartSchool", "uploads", "students");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}{ext}");
        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, ct);

        student.PhotoUrl = $"/uploads/students/{id}{ext}";
        await _uow.SaveChangesAsync(ct);
        return OkEnvelope(new { photoUrl = student.PhotoUrl });
    }

    /// <summary>Paged, searchable student list (staff & teachers only).</summary>
    [HttpGet]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<PagedResult<StudentListItem>>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? classId,
        [FromQuery] int? busId,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.Students.Query().AsQueryable();
        if (!includeInactive)
            query = query.Where(s => s.IsActive);
        if (classId is not null)
            query = query.Where(s => s.ClassId == classId);
        if (busId is not null)
            query = query.Where(s => s.BusId == busId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(s =>
                EF.Functions.Like(s.FullName, pattern) ||
                EF.Functions.Like(s.RollNo, pattern) ||
                EF.Functions.Like(s.FatherName, pattern) ||
                EF.Functions.Like(s.FatherPhone, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new StudentListItem(
                s.Id, s.FullName, s.RollNo,
                s.Class.Name + "-" + s.Class.Section, s.ClassId,
                s.Bus != null ? s.Bus.BusNumber : null,
                s.FatherName, s.FatherPhone, s.PhotoUrl, s.IsActive))
            .ToListAsync(ct);

        return OkEnvelope(PagedResult<StudentListItem>.Create(items, page, pageSize, total));
    }

    /// <summary>Student detail (staff/teachers, or the parent of this child).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<StudentDetailResponse>>> GetById(int id, CancellationToken ct)
    {
        if (IsParent && !await ParentOwnsStudentAsync(id, ct))
            return Forbid();

        var s = await _uow.Students.Query()
            .Include(x => x.Class)
            .Include(x => x.Bus)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
            return NotFoundEnvelope($"Student {id} was not found.");

        var outstanding = await _uow.Fees.Query()
            .Where(f => f.StudentId == id && f.Status != FeeStatus.Paid)
            .SumAsync(f => (decimal?)f.Amount, ct) ?? 0m;

        var recentRaw = await _uow.BusAttendances.Query()
            .Where(e => e.StudentId == id)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(5)
            .Select(e => new { e.Id, e.Type, e.TimestampUtc, e.LocationLabel, BusNumber = e.Bus.BusNumber })
            .ToListAsync(ct);

        var recent = recentRaw
            .Select(e => new StudentRecentEvent(
                e.Id, e.Type.ToString(),
                PkTime.ToPk(e.TimestampUtc).ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture),
                e.BusNumber, e.LocationLabel))
            .ToList();

        return OkEnvelope(new StudentDetailResponse(
            s.Id, s.FullName, s.DateOfBirth, s.RollNo,
            s.ClassId, s.Class.DisplayName,
            s.Bus?.BusNumber, s.BusId,
            s.FatherName, s.FatherPhone, s.MotherPhone, s.Address,
            s.PhotoUrl, s.IsActive, outstanding, recent));
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<StudentListItem>>> Create(StudentCreateRequest request, CancellationToken ct)
    {
        if (!await _uow.Classes.AnyAsync(c => c.Id == request.ClassId, ct))
            return BadRequestEnvelope($"Class {request.ClassId} does not exist.");
        if (request.BusId is not null && !await _uow.Buses.AnyAsync(b => b.Id == request.BusId, ct))
            return BadRequestEnvelope($"Bus {request.BusId} does not exist.");

        var student = new Student
        {
            FullName = request.FullName.Trim(),
            DateOfBirth = request.DateOfBirth,
            ClassId = request.ClassId,
            RollNo = request.RollNo.Trim(),
            PhotoUrl = request.PhotoUrl,
            FatherName = request.FatherName.Trim(),
            FatherPhone = request.FatherPhone.Trim(),
            MotherPhone = request.MotherPhone?.Trim(),
            Address = request.Address.Trim(),
            BusId = request.BusId,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await _uow.Students.AddAsync(student, ct);
        await _uow.SaveChangesAsync(ct);

        var item = await _uow.Students.Query()
            .Where(s => s.Id == student.Id)
            .Select(s => new StudentListItem(
                s.Id, s.FullName, s.RollNo,
                s.Class.Name + "-" + s.Class.Section, s.ClassId,
                s.Bus != null ? s.Bus.BusNumber : null,
                s.FatherName, s.FatherPhone, s.PhotoUrl, s.IsActive))
            .FirstAsync(ct);

        return CreatedEnvelope(item, "Student created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, StudentUpdateRequest request, CancellationToken ct)
    {
        var student = await _uow.Students.FindAsync(new object[] { id }, ct);
        if (student is null)
            return NotFoundEnvelope($"Student {id} was not found.");

        if (!await _uow.Classes.AnyAsync(c => c.Id == request.ClassId, ct))
            return BadRequestEnvelope($"Class {request.ClassId} does not exist.");
        if (request.BusId is not null && !await _uow.Buses.AnyAsync(b => b.Id == request.BusId, ct))
            return BadRequestEnvelope($"Bus {request.BusId} does not exist.");

        student.FullName = request.FullName.Trim();
        student.DateOfBirth = request.DateOfBirth;
        student.ClassId = request.ClassId;
        student.RollNo = request.RollNo.Trim();
        student.PhotoUrl = request.PhotoUrl;
        student.FatherName = request.FatherName.Trim();
        student.FatherPhone = request.FatherPhone.Trim();
        student.MotherPhone = request.MotherPhone?.Trim();
        student.Address = request.Address.Trim();
        student.BusId = request.BusId;
        student.IsActive = request.IsActive;

        await _uow.SaveChangesAsync(ct);
        return OkMessage("Student updated successfully.");
    }

    /// <summary>Soft delete (deactivation) - history is preserved.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Deactivate(int id, CancellationToken ct)
    {
        var student = await _uow.Students.FindAsync(new object[] { id }, ct);
        if (student is null)
            return NotFoundEnvelope($"Student {id} was not found.");

        student.IsActive = false;
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Student deactivated successfully.");
    }
}
