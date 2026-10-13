using SmartSchool.Shared.Enums;

namespace SmartSchool.Core.Models;

public sealed class Fee
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>Billing month as "yyyy-MM", e.g. "2026-10".</summary>
    public string Month { get; set; } = string.Empty;

    public DateOnly DueDate { get; set; }

    public DateOnly? PaidDate { get; set; }

    public FeeStatus Status { get; set; } = FeeStatus.Pending;

    /// <summary>Generated on payment: INV-{yyyyMM}-{feeId:D6}.</summary>
    public string? ReceiptNo { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
