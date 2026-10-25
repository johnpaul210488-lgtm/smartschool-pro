using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers; // <-- CHANGED: use Pakistan-local effective dates for optional opt-ins.

namespace SmartSchool.API.Controllers;

/// <summary>Manages configurable fee heads and draft class plans; saving a plan never creates or charges invoices.</summary>
[ApiController]
[Route("api/fee-plans")]
[Authorize(Roles = AppRoles.Staff)]
public sealed class FeePlansController : BaseApiController
{
    private const decimal MaximumHeadAmount = 10_000_000m;
    private readonly SchoolDbContext _db;

    public FeePlansController(SchoolDbContext db) => _db = db;

    [HttpGet("heads")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FeeHeadDto>>>> GetHeads(CancellationToken ct)
    {
        var heads = await _db.FeeHeads.AsNoTracking()
            .Where(h => h.IsActive)
            .OrderBy(h => h.Category).ThenBy(h => h.Name)
            .Select(h => new FeeHeadDto(h.Id, h.Code, h.Name, h.Category, h.DefaultCycle, h.IsOptionalForStudent))
            .ToListAsync(ct);
        return OkEnvelope<IReadOnlyList<FeeHeadDto>>(heads);
    }

    [HttpGet("classes/{classId:int}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClassFeePlanItemDto>>>> GetClassPlan(
        int classId, CancellationToken ct)
    {
        if (!await _db.Classes.AnyAsync(c => c.Id == classId, ct))
            return NotFoundEnvelope($"Class {classId} was not found.");

        var heads = await _db.FeeHeads.AsNoTracking().Where(h => h.IsActive)
            .OrderBy(h => h.Category).ThenBy(h => h.Name).ToListAsync(ct);
        var lines = await _db.ClassFeePlanItems.AsNoTracking()
            .Where(p => p.ClassId == classId).ToDictionaryAsync(p => p.FeeHeadId, ct);

        var items = heads.Select(h =>
        {
            lines.TryGetValue(h.Id, out var line);
            return new ClassFeePlanItemDto(
                h.Id, h.Code, h.Name, h.Category, h.DefaultCycle,
                line?.BillingCycle ?? h.DefaultCycle, line?.Amount ?? 0m,
                line?.IsIncluded ?? false, line?.IsOptionalForStudent ?? h.IsOptionalForStudent);
        }).ToList();

        return OkEnvelope<IReadOnlyList<ClassFeePlanItemDto>>(items);
    }

    [HttpPut("classes/{classId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> SaveClassPlan(
        int classId, SaveClassFeePlanRequest request, CancellationToken ct)
    {
        if (!await _db.Classes.AnyAsync(c => c.Id == classId, ct))
            return NotFoundEnvelope($"Class {classId} was not found.");

        var posted = request.Items ?? new List<SaveClassFeePlanItemRequest>();
        if (posted.Select(x => x.FeeHeadId).Distinct().Count() != posted.Count)
            return BadRequestEnvelope("Each fee head may appear only once in a class plan.");
        if (posted.Any(x => x.FeeHeadId <= 0 || x.Amount < 0 || x.Amount > MaximumHeadAmount ||
                            !Enum.IsDefined(x.BillingCycle)))
            return BadRequestEnvelope("Fee head, billing cycle, or amount is invalid.");

        var activeHeadIds = await _db.FeeHeads.Where(h => h.IsActive).Select(h => h.Id).ToListAsync(ct);
        if (posted.Any(x => !activeHeadIds.Contains(x.FeeHeadId)))
            return BadRequestEnvelope("The plan contains an unknown or inactive fee head.");

        var existing = await _db.ClassFeePlanItems.Where(p => p.ClassId == classId).ToListAsync(ct);
        var byHead = existing.ToDictionary(p => p.FeeHeadId);
        foreach (var item in posted)
        {
            if (!byHead.TryGetValue(item.FeeHeadId, out var line))
            {
                line = new ClassFeePlanItem { ClassId = classId, FeeHeadId = item.FeeHeadId };
                _db.ClassFeePlanItems.Add(line);
                byHead[item.FeeHeadId] = line;
            }
            line.Amount = item.Amount;
            line.BillingCycle = item.BillingCycle;
            line.IsIncluded = item.IsIncluded;
            line.IsOptionalForStudent = item.IsOptionalForStudent;
            line.EffectiveFrom = PkTime.TodayPk; // <-- CHANGED: plan edits affect only periods that start from today onward.
        }

        // Omitted heads stay stored for later review, but are excluded from the active draft plan.
        foreach (var line in existing.Where(x => !posted.Any(p => p.FeeHeadId == x.FeeHeadId)))
            line.IsIncluded = false;

        await _db.SaveChangesAsync(ct);
        return OkEnvelope<object>(new { classId, configuredHeads = posted.Count }, "Draft class fee plan saved. No fees were generated.");
    }

    // CHANGED: class/student plan activation and recurrence calendar are configured explicitly by staff.
    [HttpGet("assignments/class/{classId:int}")]
    public async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> GetClassAssignment(int classId, CancellationToken ct)
        => await GetAssignmentAsync(x => x.ClassId == classId, ct);

    [HttpPut("assignments/class/{classId:int}")]
    public async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> SaveClassAssignment(
        int classId, SaveFeePlanAssignmentRequest request, CancellationToken ct)
    {
        if (!await _db.Classes.AnyAsync(x => x.Id == classId, ct)) return NotFoundEnvelope($"Class {classId} was not found.");
        return await SaveAssignmentAsync(x => x.ClassId == classId, new FeePlanAssignment { ClassId = classId }, request, ct);
    }

    [HttpGet("assignments/student/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> GetStudentAssignment(int studentId, CancellationToken ct)
        => await GetAssignmentAsync(x => x.StudentId == studentId, ct);

    [HttpPut("assignments/student/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> SaveStudentAssignment(
        int studentId, SaveFeePlanAssignmentRequest request, CancellationToken ct)
    {
        if (!await _db.Students.AnyAsync(x => x.Id == studentId && x.IsActive, ct))
            return NotFoundEnvelope($"Active student {studentId} was not found.");
        return await SaveAssignmentAsync(x => x.StudentId == studentId, new FeePlanAssignment { StudentId = studentId }, request, ct);
    }

    private async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> GetAssignmentAsync(
        System.Linq.Expressions.Expression<Func<FeePlanAssignment, bool>> predicate, CancellationToken ct)
    {
        var value = await _db.FeePlanAssignments.AsNoTracking().FirstOrDefaultAsync(predicate, ct);
        return OkEnvelope(ToAssignmentDto(value));
    }

    private async Task<ActionResult<ApiResponse<FeePlanAssignmentDto>>> SaveAssignmentAsync(
        System.Linq.Expressions.Expression<Func<FeePlanAssignment, bool>> predicate,
        FeePlanAssignment newValue, SaveFeePlanAssignmentRequest request, CancellationToken ct)
    {
        if (request.DueDay is < 1 or > 28 || request.QuarterStartMonth is < 1 or > 12 || request.SchoolYearStartMonth is < 1 or > 12)
            return BadRequestEnvelope("Due day must be 1–28 and billing start months must be 1–12.");
        if (request.IsActive && request.EffectiveFrom < PkTime.TodayPk)
            return BadRequestEnvelope("Backdated activation is blocked. Choose today or a future effective date to prevent old-period bills.");
        var value = await _db.FeePlanAssignments.FirstOrDefaultAsync(predicate, ct);
        if (value is null) { value = newValue; _db.FeePlanAssignments.Add(value); }
        value.IsActive = request.IsActive;
        value.EffectiveFrom = request.EffectiveFrom;
        value.DueDay = request.DueDay;
        value.QuarterStartMonth = request.QuarterStartMonth;
        value.SchoolYearStartMonth = request.SchoolYearStartMonth;
        value.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return OkEnvelope(ToAssignmentDto(value), request.IsActive ? "Plan activated. New bills are generated from the effective date." : "Plan saved inactive.");
    }

    private static FeePlanAssignmentDto ToAssignmentDto(FeePlanAssignment? x)
        => x is null
            ? new FeePlanAssignmentDto(false, PkTime.TodayPk, 10, 1, 4)
            : new FeePlanAssignmentDto(x.IsActive, x.EffectiveFrom, x.DueDay, x.QuarterStartMonth, x.SchoolYearStartMonth);

    // CHANGED: student search results can use this merged view to show class defaults and per-head overrides.
    [HttpGet("students/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StudentFeePlanItemDto>>>> GetStudentPlan(int studentId, CancellationToken ct)
    {
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId && s.IsActive, ct);
        if (student is null) return NotFoundEnvelope($"Active student {studentId} was not found.");
        var classRows = await _db.ClassFeePlanItems.AsNoTracking().Where(x => x.ClassId == student.ClassId)
            .ToDictionaryAsync(x => x.FeeHeadId, ct);
        var studentRows = await _db.StudentFeePlanItems.AsNoTracking().Where(x => x.StudentId == studentId)
            .ToDictionaryAsync(x => x.FeeHeadId, ct);
        var heads = await _db.FeeHeads.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(ct);
        var items = heads.Select(h =>
        {
            classRows.TryGetValue(h.Id, out var baseRow);
            studentRows.TryGetValue(h.Id, out var custom);
            return new StudentFeePlanItemDto
            {
                FeeHeadId = h.Id, Code = h.Code, Name = h.Name, Category = h.Category,
                IsOptionalForStudent = baseRow?.IsOptionalForStudent ?? h.IsOptionalForStudent,
                IsOverridden = custom?.IsEnabled == true,
                IsOptedIn = custom?.IsOptedIn ?? false,
                IsIncluded = custom?.IsEnabled == true ? (custom.IsIncludedOverride ?? false) : (baseRow?.IsIncluded ?? false),
                Amount = custom?.IsEnabled == true ? (custom.AmountOverride ?? 0m) : (baseRow?.Amount ?? 0m),
                BillingCycle = custom?.IsEnabled == true ? (custom.BillingCycleOverride ?? baseRow?.BillingCycle ?? h.DefaultCycle).ToString() : (baseRow?.BillingCycle ?? h.DefaultCycle).ToString()
            };
        }).ToList();
        return OkEnvelope<IReadOnlyList<StudentFeePlanItemDto>>(items);
    }

    [HttpPut("students/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> SaveStudentPlan(
        int studentId, SaveStudentFeePlanRequest request, CancellationToken ct)
    {
        if (!await _db.Students.AnyAsync(x => x.Id == studentId && x.IsActive, ct))
            return NotFoundEnvelope($"Active student {studentId} was not found.");
        if (request.Items.Select(x => x.FeeHeadId).Distinct().Count() != request.Items.Count)
            return BadRequestEnvelope("Each fee head may appear only once in a student plan.");
        var heads = await _db.FeeHeads.Where(x => x.IsActive).Select(x => x.Id).ToListAsync(ct);
        if (request.Items.Any(x => !heads.Contains(x.FeeHeadId) || x.Amount < 0 || x.Amount > MaximumHeadAmount ||
            !Enum.TryParse<FeeBillingCycle>(x.BillingCycle, true, out _)))
            return BadRequestEnvelope("A fee head, amount, or billing cycle is invalid.");
        var rows = await _db.StudentFeePlanItems.Where(x => x.StudentId == studentId).ToListAsync(ct);
        var byHead = rows.ToDictionary(x => x.FeeHeadId);
        foreach (var item in request.Items)
        {
            var hasOverride = item.IsOverridden || item.IsOptedIn;
            if (!byHead.TryGetValue(item.FeeHeadId, out var row))
            {
                if (!hasOverride) continue;
                row = new StudentFeePlanItem { StudentId = studentId, FeeHeadId = item.FeeHeadId };
                _db.StudentFeePlanItems.Add(row);
            }
            if (!hasOverride) { _db.StudentFeePlanItems.Remove(row); continue; }
            row.IsEnabled = item.IsOverridden;
            row.IsOptedIn = item.IsOptedIn;
            row.EffectiveFrom = PkTime.TodayPk; // <-- CHANGED: edits affect future billing periods only.
            row.AmountOverride = item.IsOverridden ? item.Amount : null;
            row.BillingCycleOverride = item.IsOverridden && Enum.TryParse<FeeBillingCycle>(item.BillingCycle, true, out var cycle) ? cycle : null;
            row.IsIncludedOverride = item.IsOverridden ? item.IsIncluded : null;
        }
        await _db.SaveChangesAsync(ct);
        return OkEnvelope<object>(new { studentId, savedHeads = request.Items.Count }, "Student-specific fee choices saved. Future bills only.");
    }
}
