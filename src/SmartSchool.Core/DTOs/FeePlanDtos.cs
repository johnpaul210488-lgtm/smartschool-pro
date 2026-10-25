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
    bool IsOptionalForStudent,
    string? DisplayName = null);

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

/// <summary>Staff-added fee head (manual row) that can then be ticked in any plan.</summary>
public sealed class CustomFeeHeadRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
}

/// <summary>Class-specific wording for one fee head row. Empty value resets to the catalogue name.</summary>
public sealed class RenameClassFeeHeadRequest
{
    public string? DisplayName { get; set; }
}
