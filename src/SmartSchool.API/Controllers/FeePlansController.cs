using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.DTOs;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Common;
using SmartSchool.Shared.Constants;
using SmartSchool.Shared.Enums;

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
        }

        // Omitted heads stay stored for later review, but are excluded from the active draft plan.
        foreach (var line in existing.Where(x => !posted.Any(p => p.FeeHeadId == x.FeeHeadId)))
            line.IsIncluded = false;

        await _db.SaveChangesAsync(ct);
        return OkEnvelope<object>(new { classId, configuredHeads = posted.Count }, "Draft class fee plan saved. No fees were generated.");
    }
}
