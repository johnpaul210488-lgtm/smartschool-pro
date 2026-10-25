namespace SmartSchool.Core.DTOs;

/// <summary>
/// Simple "add fee to balance" request from the Fee Plan screen.
/// Scope = "Class" (every active student of ClassId) or "Student" (only StudentId).
/// Cycle = "Monthly" | "Quarterly" | "Annually" | "OneTime".
/// Each line becomes one bill per student; a fee already billed for the same
/// student + fee head + period is skipped (never duplicated).
/// </summary>
public sealed class FeeChargeRequest
{
    public string Scope { get; set; } = "Student";
    public int? ClassId { get; set; }
    public int? StudentId { get; set; }
    public string Cycle { get; set; } = "Monthly";
    /// <summary>Billing month "yyyy-MM" (for Annually = the school-year start month).</summary>
    public string PeriodMonth { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public List<FeeChargeLine> Items { get; set; } = new();
}

public sealed class FeeChargeLine
{
    public int FeeHeadId { get; set; }
    public decimal Amount { get; set; }
}

public sealed record FeeChargeResult(int StudentCount, int Created, int Skipped);

/// <summary>One bill line in a student's full fee history.</summary>
public sealed record FeeHistoryRow(
    int FeeId,
    string Month,
    string FeeHead,
    string Cycle,
    DateOnly DueDate,
    decimal Amount,
    decimal AmountPaid,
    decimal Balance,
    string Status,
    string? ReceiptNo);

/// <summary>One payment transaction, with the exact date and time (Pakistan time).</summary>
public sealed record FeeHistoryPayment(
    int PaymentId,
    int FeeId,
    string FeeHead,
    string Month,
    DateTime PaidAtPkt,
    decimal Amount,
    string ReceiptNo,
    string? Method,
    string? Notes);

public sealed record StudentFeeHistoryDto(
    int StudentId,
    string StudentName,
    string ClassName,
    string RollNo,
    string FatherName,
    string FatherPhone,
    decimal TotalAmount,
    decimal TotalPaid,
    decimal TotalBalance,
    IReadOnlyList<FeeHistoryRow> Fees,
    IReadOnlyList<FeeHistoryPayment> Payments);
