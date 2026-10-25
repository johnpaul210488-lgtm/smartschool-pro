using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>Per-head student override. Nullable values inherit the active class plan.</summary>
public sealed class StudentFeePlanItem
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int FeeHeadId { get; set; }
    public FeeHead FeeHead { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public decimal? AmountOverride { get; set; }
    public FeeBillingCycle? BillingCycleOverride { get; set; }
    public bool? IsIncludedOverride { get; set; }
    public bool IsOptedIn { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
}
