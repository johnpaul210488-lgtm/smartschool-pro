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
public sealed class FeesController : BaseApiController
{
    private readonly IUnitOfWork _uow;

    public FeesController(IUnitOfWork uow) => _uow = uow;

    private static string EffectiveStatus(FeeStatus status, DateOnly dueDate)
        => status == FeeStatus.Pending && dueDate < PkTime.TodayPk
            ? nameof(FeeStatus.Overdue)
            : status.ToString();

    private static FeeListItem ToDto(Fee f) => new(
        f.Id, f.StudentId, f.Student.FullName,
        f.Student.Class.Name + "-" + f.Student.Class.Section,
        f.Amount, f.Month, f.DueDate, f.PaidDate,
        EffectiveStatus(f.Status, f.DueDate), f.ReceiptNo, f.Notes);

    [HttpGet]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<PagedResult<FeeListItem>>>> GetAll(
        [FromQuery] int? studentId,
        [FromQuery] string? month,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _uow.Fees.Query()
            .Include(f => f.Student).ThenInclude(s => s.Class)
            .AsQueryable();

        if (studentId is not null)
            query = query.Where(f => f.StudentId == studentId);
        if (!string.IsNullOrWhiteSpace(month))
            query = query.Where(f => f.Month == month);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<FeeStatus>(status, true, out var parsed))
            query = query.Where(f => f.Status == parsed);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(f => f.DueDate).ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return OkEnvelope(PagedResult<FeeListItem>.Create(rows.Select(ToDto).ToList(), page, pageSize, total));
    }

    /// <summary>Fees of one student - staff/teachers, or that student's parent.</summary>
    [HttpGet("student/{studentId:int}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FeeListItem>>>> GetByStudent(
        int studentId, CancellationToken ct)
    {
        if (IsParent)
        {
            var owns = await _uow.ParentStudents.AnyAsync(
                ps => ps.ParentUserId == CallerUserId && ps.StudentId == studentId, ct);
            if (!owns)
                return Forbid();
        }
        else if (!IsStaff && CallerRole != AppRoles.Teacher)
        {
            return Forbid();
        }

        var rows = await _uow.Fees.Query()
            .Include(f => f.Student).ThenInclude(s => s.Class)
            .Where(f => f.StudentId == studentId)
            .OrderByDescending(f => f.Month)
            .ToListAsync(ct);

        return OkEnvelope<IReadOnlyList<FeeListItem>>(rows.Select(ToDto).ToList());
    }

    /// <summary>Everyone who still owes money (Pending + Partial + past-due), with totals.</summary>
    [HttpGet("dues")]
    [Authorize(Roles = AppRoles.StaffAndTeachers)]
    public async Task<ActionResult<ApiResponse<DuesSummaryResponse>>> GetDues(
        [FromQuery] int? classId,
        [FromQuery] string? month,
        CancellationToken ct = default)
    {
        var query = _uow.Fees.Query()
            .Include(f => f.Student).ThenInclude(s => s.Class)
            .Where(f => f.Status != FeeStatus.Paid);

        if (classId is not null)
            query = query.Where(f => f.Student.ClassId == classId);
        if (!string.IsNullOrWhiteSpace(month))
            query = query.Where(f => f.Month == month);

        var rows = await query.OrderBy(f => f.DueDate).Take(500).ToListAsync(ct);
        var items = rows.Select(ToDto).ToList();

        return OkEnvelope(new DuesSummaryResponse(items.Count, items.Sum(i => i.Amount), items));
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<FeeListItem>>> Create(FeeCreateRequest request, CancellationToken ct)
    {
        var student = await _uow.Students.Query(false).Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null)
            return BadRequestEnvelope($"Student {request.StudentId} does not exist.");

        var fee = new Fee
        {
            StudentId = student.Id,
            Amount = request.Amount,
            Month = request.Month,
            DueDate = request.DueDate,
            Notes = request.Notes,
            Status = FeeStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        await _uow.Fees.AddAsync(fee, ct);
        await _uow.SaveChangesAsync(ct);

        fee.Student = student;
        return CreatedEnvelope(ToDto(fee), "Fee created successfully.");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Update(int id, FeeUpdateRequest request, CancellationToken ct)
    {
        var fee = await _uow.Fees.FindAsync(new object[] { id }, ct);
        if (fee is null)
            return NotFoundEnvelope($"Fee {id} was not found.");
        if (fee.Status == FeeStatus.Paid)
            return ConflictEnvelope("A paid fee cannot be edited.");

        fee.Amount = request.Amount;
        fee.Month = request.Month;
        fee.DueDate = request.DueDate;
        fee.Notes = request.Notes;

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<FeeStatus>(request.Status, true, out var status))
            fee.Status = status;

        await _uow.SaveChangesAsync(ct);
        return OkMessage("Fee updated successfully.");
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var fee = await _uow.Fees.FindAsync(new object[] { id }, ct);
        if (fee is null)
            return NotFoundEnvelope($"Fee {id} was not found.");
        if (fee.Status == FeeStatus.Paid)
            return ConflictEnvelope("A paid fee cannot be deleted (keep it for the receipt trail).");

        _uow.Fees.Remove(fee);
        await _uow.SaveChangesAsync(ct);
        return OkMessage("Fee deleted successfully.");
    }

    /// <summary>Mark a fee as paid; generates receipt INV-{yyyyMM}-{id:D6}.</summary>
    [HttpPost("{id:int}/pay")]
    [Authorize(Roles = AppRoles.Staff)]
    public async Task<ActionResult<ApiResponse<FeeListItem>>> Pay(int id, PayFeeRequest? request, CancellationToken ct)
    {
        var fee = await _uow.Fees.Query(false)
            .Include(f => f.Student).ThenInclude(s => s.Class)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fee is null)
            return NotFoundEnvelope($"Fee {id} was not found.");
        if (fee.Status == FeeStatus.Paid)
            return ConflictEnvelope($"Fee {id} is already paid (receipt {fee.ReceiptNo}).");

        var paidDate = request?.PaidDate ?? PkTime.TodayPk;
        fee.Status = FeeStatus.Paid;
        fee.PaidDate = paidDate;
        fee.ReceiptNo = $"INV-{paidDate:yyyyMM}-{fee.Id:D6}";
        await _uow.SaveChangesAsync(ct);

        return OkEnvelope(ToDto(fee), $"Payment recorded. Receipt no: {fee.ReceiptNo}");
    }
}
