namespace SmartSchool.Core.Models;

/// <summary>A single payment transaction against a fee; never overwritten by later payments.</summary>
public sealed class FeePayment
{
    public int Id { get; set; }
    public int FeeId { get; set; }
    public Fee Fee { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidDate { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
