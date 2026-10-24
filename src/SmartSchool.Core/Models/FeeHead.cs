using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

/// <summary>Reusable fee type; amounts belong to class plans, never to the catalogue.</summary>
public sealed class FeeHead
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public FeeBillingCycle DefaultCycle { get; set; } = FeeBillingCycle.Monthly;
    public bool IsOptionalForStudent { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ClassFeePlanItem> ClassPlanItems { get; set; } = new List<ClassFeePlanItem>();
}
