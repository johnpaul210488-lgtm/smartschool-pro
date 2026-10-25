using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.DTOs;

public sealed record FeePlanAssignmentDto(
    bool IsActive,
    DateOnly EffectiveFrom,
    int DueDay,
    int QuarterStartMonth,
    int SchoolYearStartMonth);

public sealed class SaveFeePlanAssignmentRequest
{
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public int DueDay { get; set; }
    public int QuarterStartMonth { get; set; }
    public int SchoolYearStartMonth { get; set; }
}

public sealed class StudentFeePlanItemDto
{
    public int FeeHeadId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsOptionalForStudent { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsOptedIn { get; set; }
    public bool IsIncluded { get; set; }
    public decimal Amount { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
}

public sealed class SaveStudentFeePlanRequest
{
    public List<SaveStudentFeePlanItemRequest> Items { get; set; } = new();
}

public sealed class SaveStudentFeePlanItemRequest
{
    public int FeeHeadId { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsOptedIn { get; set; }
    public bool IsIncluded { get; set; }
    public decimal Amount { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
}

public sealed record FeePaymentDto(
    int Id,
    int FeeId,
    decimal Amount,
    DateOnly PaidDate,
    string ReceiptNo,
    string? Method,
    string? Notes);

public sealed class RecordFeePaymentRequest
{
    public decimal Amount { get; set; }
    public DateOnly? PaidDate { get; set; }
    public string? Method { get; set; }
    public string? Notes { get; set; }
}
