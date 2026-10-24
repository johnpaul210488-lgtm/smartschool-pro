using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.DTOs;

public sealed record FeeHeadDto(
    int Id,
    string Code,
    string Name,
    string Category,
    FeeBillingCycle DefaultCycle,
    bool IsOptionalForStudent);

public sealed record ClassFeePlanItemDto(
    int FeeHeadId,
    string Code,
    string Name,
    string Category,
    FeeBillingCycle DefaultCycle,
    FeeBillingCycle BillingCycle,
    decimal Amount,
    bool IsIncluded,
    bool IsOptionalForStudent);

public sealed class SaveClassFeePlanRequest
{
    public List<SaveClassFeePlanItemRequest> Items { get; set; } = new();
}

public sealed class SaveClassFeePlanItemRequest
{
    public int FeeHeadId { get; set; }
    public FeeBillingCycle BillingCycle { get; set; }
    public decimal Amount { get; set; }
    public bool IsIncluded { get; set; }
    public bool IsOptionalForStudent { get; set; }
}
