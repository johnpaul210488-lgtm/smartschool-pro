namespace SmartSchool.Core.DTOs;

public sealed record FeeListItem(
    int Id,
    int StudentId,
    string StudentName,
    string ClassName,
    decimal Amount,
    string Month,
    DateOnly DueDate,
    DateOnly? PaidDate,
    // Effective status: "Overdue" is computed on the fly for unpaid past-due rows.
    string Status,
    string? ReceiptNo,
    string? Notes);

public class FeeCreateRequest
{
    public int StudentId { get; set; }
    public decimal Amount { get; set; }
    public string Month { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string? Notes { get; set; }
}

public sealed class FeeUpdateRequest : FeeCreateRequest
{
    /// <summary>Optional force-status (Pending/Paid/Overdue/Partial).</summary>
    public string? Status { get; set; }
}

public sealed class PayFeeRequest
{
    /// <summary>Defaults to today (Pakistan time).</summary>
    public DateOnly? PaidDate { get; set; }
}

public sealed record DuesSummaryResponse(
    int TotalUnpaidCount,
    decimal TotalUnpaidAmount,
    IReadOnlyList<FeeListItem> Items);
