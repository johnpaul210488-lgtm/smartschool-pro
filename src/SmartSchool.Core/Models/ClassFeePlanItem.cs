using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>Editable current template for one fee head in one class; existing fee records remain snapshots.</summary>
public sealed class ClassFeePlanItem
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public SchoolClass Class { get; set; } = null!;
    public int FeeHeadId { get; set; }
    public FeeHead FeeHead { get; set; } = null!;
    public decimal Amount { get; set; }
    public FeeBillingCycle BillingCycle { get; set; }
    public bool IsIncluded { get; set; }
    public bool IsOptionalForStudent { get; set; }
}
