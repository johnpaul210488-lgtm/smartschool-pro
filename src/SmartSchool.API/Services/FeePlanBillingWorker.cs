using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Models;
using SmartSchool.Infrastructure.Data;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Services;

/// <summary>Idempotently issues one fee record per active student/head/billing period.</summary>
public sealed class FeePlanBillingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FeePlanBillingWorker> _logger;

    public FeePlanBillingWorker(IServiceScopeFactory scopes, ILogger<FeePlanBillingWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Recurring fee-plan generation failed; it will retry next cycle."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var today = PkTime.TodayPk;
        var activeAssignments = await db.FeePlanAssignments.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var classAssignments = activeAssignments.Where(x => x.ClassId.HasValue).ToDictionary(x => x.ClassId!.Value);
        var studentAssignments = activeAssignments.Where(x => x.StudentId.HasValue).ToDictionary(x => x.StudentId!.Value);
        var students = await db.Students.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var heads = await db.FeeHeads.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var classIds = classAssignments.Keys.ToList();
        var classRowsByClass = classIds.Count == 0 ? new Dictionary<int, Dictionary<int, ClassFeePlanItem>>() :
            (await db.ClassFeePlanItems.AsNoTracking().Where(x => classIds.Contains(x.ClassId)).ToListAsync(ct))
                .GroupBy(x => x.ClassId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FeeHeadId));
        var activeStudentIds = studentAssignments.Keys.ToList();
        var studentRowsByStudent = activeStudentIds.Count == 0 ? new Dictionary<int, Dictionary<int, StudentFeePlanItem>>() :
            (await db.StudentFeePlanItems.AsNoTracking().Where(x => activeStudentIds.Contains(x.StudentId)).ToListAsync(ct))
                .GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FeeHeadId));
        var studentIds = students.Select(x => x.Id).ToList();
        var existingKeys = studentIds.Count == 0 ? new HashSet<(int, int, string)>() :
            (await db.Fees.AsNoTracking().Where(x => studentIds.Contains(x.StudentId) && x.FeeHeadId != null && x.BillingPeriodKey != null)
                .Select(x => new { x.StudentId, FeeHeadId = x.FeeHeadId!.Value, Key = x.BillingPeriodKey! }).ToListAsync(ct))
                .Select(x => (x.StudentId, x.FeeHeadId, x.Key)).ToHashSet();

        foreach (var student in students)
        {
            classAssignments.TryGetValue(student.ClassId, out var classAssignment);
            studentAssignments.TryGetValue(student.Id, out var studentAssignment);
            if (classAssignment is null && studentAssignment is null) continue;
            var schedule = studentAssignment ?? classAssignment!;
            if (today < schedule.EffectiveFrom) continue;

            var classRows = classRowsByClass.GetValueOrDefault(student.ClassId) ?? new Dictionary<int, ClassFeePlanItem>();
            var studentRows = studentRowsByStudent.GetValueOrDefault(student.Id) ?? new Dictionary<int, StudentFeePlanItem>();

            foreach (var head in heads)
            {
                classRows.TryGetValue(head.Id, out var baseRow);
                studentRows.TryGetValue(head.Id, out var custom);
                var included = custom?.IsEnabled == true ? custom.IsIncludedOverride ?? false : baseRow?.IsIncluded ?? false;
                var amount = custom?.IsEnabled == true ? custom.AmountOverride ?? 0m : baseRow?.Amount ?? 0m;
                var cycle = custom?.IsEnabled == true ? custom.BillingCycleOverride ?? baseRow?.BillingCycle ?? head.DefaultCycle : baseRow?.BillingCycle ?? head.DefaultCycle;
                var optional = baseRow?.IsOptionalForStudent ?? head.IsOptionalForStudent;
                if (optional && custom?.IsOptedIn != true) continue;
                if (!included || amount <= 0m) continue;
                var itemEffectiveFrom = schedule.EffectiveFrom;
                if (baseRow?.EffectiveFrom is DateOnly baseEffective && baseEffective > itemEffectiveFrom) itemEffectiveFrom = baseEffective;
                if (custom?.EffectiveFrom is DateOnly customEffective && customEffective > itemEffectiveFrom) itemEffectiveFrom = customEffective;

                var studentCreatedDate = DateOnly.FromDateTime(student.CreatedAtUtc);
                var firstOccurrenceFrom = cycle == FeeBillingCycle.OneTime && studentCreatedDate > itemEffectiveFrom
                    ? studentCreatedDate : itemEffectiveFrom;
                foreach (var occurrence in GetDuePeriods(today, schedule, cycle, firstOccurrenceFrom))
                {
                    if (occurrence.Start < itemEffectiveFrom || occurrence.Start < studentCreatedDate) continue;
                    var key = cycle == FeeBillingCycle.OneTime ? "ONETIME" : $"{cycle}:{occurrence.Start:yyyy-MM}";
                    if (!existingKeys.Add((student.Id, head.Id, key))) continue;
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
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateException ex)
                    {
                        db.Entry(fee).State = EntityState.Detached;
                        _logger.LogInformation(ex, "Skipped duplicate plan fee for student {StudentId}, head {HeadId}, key {Key}.", student.Id, head.Id, key);
                    }
                }
            }
        }
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
                var quarterMonth = ((today.Month - schedule.QuarterStartMonth + 12) % 12);
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
        // CHANGED: issue only the current eligible period; missed older periods are never back-billed.
        if (periodStart >= effectiveFrom && periodStart <= today)
            yield return (periodStart, DueDate(periodStart, schedule.DueDay));
    }

    private static DateOnly DueDate(DateOnly periodStart, int dueDay)
    {
        var day = Math.Clamp(dueDay, 1, DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
        return new DateOnly(periodStart.Year, periodStart.Month, day);
    }
}
