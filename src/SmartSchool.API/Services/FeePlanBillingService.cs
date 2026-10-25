using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Services;

/// <summary>Outcome of one billing sweep, shown to staff after a manual run.</summary>
public sealed record FeeGenerationResult(int StudentsConsidered, int Created, int Skipped);

/// <summary>
/// Idempotently issues one fee record per active student/head/billing period.
/// Used by the background worker (every 30 minutes) and by the staff-triggered
/// "generate now" endpoint, so a freshly activated plan bills without waiting
/// for the next timer tick.
/// </summary>
public sealed class FeePlanBillingService
{
    private readonly SchoolDbContext _db;
    private readonly ILogger<FeePlanBillingService> _logger;

    public FeePlanBillingService(SchoolDbContext db, ILogger<FeePlanBillingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FeeGenerationResult> RunAsync(CancellationToken ct)
    {
        var db = _db;
        var today = PkTime.TodayPk;
        var activeAssignments = await db.FeePlanAssignments.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        if (activeAssignments.Count == 0)
            return new FeeGenerationResult(0, 0, 0);

        var classAssignments = activeAssignments.Where(x => x.ClassId.HasValue).ToDictionary(x => x.ClassId!.Value);
        var studentAssignments = activeAssignments.Where(x => x.StudentId.HasValue).ToDictionary(x => x.StudentId!.Value);
        var students = await db.Students.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var heads = await db.FeeHeads.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);

        var classIds = classAssignments.Keys.ToList();
        var classRowsByClass = classIds.Count == 0
            ? new Dictionary<int, Dictionary<int, ClassFeePlanItem>>()
            : (await db.ClassFeePlanItems.AsNoTracking().Where(x => classIds.Contains(x.ClassId)).ToListAsync(ct))
                .GroupBy(x => x.ClassId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FeeHeadId));

        var activeStudentIds = studentAssignments.Keys.ToList();
        var studentRowsByStudent = activeStudentIds.Count == 0
            ? new Dictionary<int, Dictionary<int, StudentFeePlanItem>>()
            : (await db.StudentFeePlanItems.AsNoTracking().Where(x => activeStudentIds.Contains(x.StudentId)).ToListAsync(ct))
                .GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FeeHeadId));

        var studentIds = students.Select(x => x.Id).ToList();
        var existingKeys = studentIds.Count == 0
            ? new HashSet<(int, int, string)>()
            : (await db.Fees.AsNoTracking()
                    .Where(x => studentIds.Contains(x.StudentId) && x.FeeHeadId != null && x.BillingPeriodKey != null)
                    .Select(x => new { x.StudentId, FeeHeadId = x.FeeHeadId!.Value, Key = x.BillingPeriodKey! })
                    .ToListAsync(ct))
                .Select(x => (x.StudentId, x.FeeHeadId, x.Key)).ToHashSet();

        var considered = 0;
        var created = 0;
        var skipped = 0;

        foreach (var student in students)
        {
            classAssignments.TryGetValue(student.ClassId, out var classAssignment);
            studentAssignments.TryGetValue(student.Id, out var studentAssignment);
            if (classAssignment is null && studentAssignment is null) continue;
            var schedule = studentAssignment ?? classAssignment!;
            if (today < schedule.EffectiveFrom) continue;
            considered++;

            var classRows = classRowsByClass.GetValueOrDefault(student.ClassId) ?? new Dictionary<int, ClassFeePlanItem>();
            var studentRows = studentRowsByStudent.GetValueOrDefault(student.Id) ?? new Dictionary<int, StudentFeePlanItem>();

            foreach (var head in heads)
            {
                classRows.TryGetValue(head.Id, out var baseRow);
                studentRows.TryGetValue(head.Id, out var custom);
                var overridden = custom?.IsEnabled == true;
                var included = overridden ? custom!.IsIncludedOverride ?? false : baseRow?.IsIncluded ?? false;
                var amount = overridden ? custom!.AmountOverride ?? 0m : baseRow?.Amount ?? 0m;
                var cycle = overridden
                    ? custom!.BillingCycleOverride ?? baseRow?.BillingCycle ?? head.DefaultCycle
                    : baseRow?.BillingCycle ?? head.DefaultCycle;
                var optional = baseRow?.IsOptionalForStudent ?? head.IsOptionalForStudent;

                if (optional && custom?.IsOptedIn != true) continue;
                if (!included || amount <= 0m) continue;

                var itemEffectiveFrom = schedule.EffectiveFrom;
                if (baseRow?.EffectiveFrom is DateOnly baseEffective && baseEffective > itemEffectiveFrom)
                    itemEffectiveFrom = baseEffective;
                if (custom?.EffectiveFrom is DateOnly customEffective && customEffective > itemEffectiveFrom)
                    itemEffectiveFrom = customEffective;

                var studentCreatedDate = DateOnly.FromDateTime(student.CreatedAtUtc);
                var firstOccurrenceFrom = cycle == FeeBillingCycle.OneTime && studentCreatedDate > itemEffectiveFrom
                    ? studentCreatedDate
                    : itemEffectiveFrom;

                foreach (var occurrence in GetDuePeriods(today, schedule, cycle, firstOccurrenceFrom))
                {
                    if (occurrence.Start < itemEffectiveFrom || occurrence.Start < studentCreatedDate) continue;
                    var key = cycle == FeeBillingCycle.OneTime ? "ONETIME" : $"{cycle}:{occurrence.Start:yyyy-MM}";
                    if (!existingKeys.Add((student.Id, head.Id, key))) { skipped++; continue; }

                    var fee = new Fee
                    {
                        StudentId = student.Id,
                        FeeHeadId = head.Id,
                        FeeHeadNameSnapshot = head.Name,
                        BillingCycle = cycle,
                        BillingPeriodKey = key,
                        IsAutoGenerated = true,
                        Amount = amount,
                        AmountPaid = 0m,
                        Month = occurrence.Start.ToString("yyyy-MM"),
                        DueDate = occurrence.DueDate,
                        Notes = "Automatically generated from the active fee plan.",
                        Status = FeeStatus.Pending,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    db.Fees.Add(fee);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        created++;
                    }
                    catch (DbUpdateException ex)
                    {
                        // Unique index (student, head, period) — another server/tick won the race.
                        db.Entry(fee).State = EntityState.Detached;
                        skipped++;
                        _logger.LogInformation(ex, "Skipped duplicate plan fee for student {StudentId}, head {HeadId}, key {Key}.",
                            student.Id, head.Id, key);
                    }
                }
            }
        }

        if (created > 0)
            _logger.LogInformation("Fee plan generation created {Created} fee row(s) for {Students} student(s).", created, considered);
        return new FeeGenerationResult(considered, created, skipped);
    }

    private static IEnumerable<(DateOnly Start, DateOnly DueDate)> GetDuePeriods(
        DateOnly today, FeePlanAssignment schedule, FeeBillingCycle cycle, DateOnly effectiveFrom)
    {
        if (cycle == FeeBillingCycle.OneTime)
        {
            var oneTimeDue = DueDate(effectiveFrom, schedule.DueDay);
            yield return (effectiveFrom, oneTimeDue < effectiveFrom ? effectiveFrom : oneTimeDue);
            yield break;
        }

        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        DateOnly periodStart;
        switch (cycle)
        {
            case FeeBillingCycle.Monthly:
                periodStart = currentMonth;
                break;
            case FeeBillingCycle.Quarterly:
            case FeeBillingCycle.PerTerm:
                var quarterMonth = (today.Month - schedule.QuarterStartMonth + 12) % 12;
                periodStart = currentMonth.AddMonths(-(quarterMonth % 3));
                if (periodStart.Month > today.Month) periodStart = periodStart.AddYears(-1);
                break;
            case FeeBillingCycle.Annually:
                periodStart = new DateOnly(today.Year, schedule.SchoolYearStartMonth, 1);
                if (periodStart > currentMonth) periodStart = periodStart.AddYears(-1);
                break;
            default:
                yield break;
        }

        // Only the current eligible period is issued; missed older periods are never back-billed.
        if (periodStart >= effectiveFrom && periodStart <= today)
            yield return (periodStart, DueDate(periodStart, schedule.DueDay));
    }

    private static DateOnly DueDate(DateOnly periodStart, int dueDay)
    {
        var day = Math.Clamp(dueDay, 1, DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
        return new DateOnly(periodStart.Year, periodStart.Month, day);
    }
}
