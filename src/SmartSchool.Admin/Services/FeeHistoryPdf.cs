using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Builds the printable / shareable fee statement (PDF) of one student and the
/// WhatsApp text that goes with it. Files are saved under Documents\SmartSchool\Fee Statements.
/// </summary>
public static class FeeHistoryPdf
{
    static FeeHistoryPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SmartSchool", "Fee Statements");

    public static string Create(StudentFeeHistoryDto h, string folder)
    {
        Directory.CreateDirectory(folder);
        var safeName = SafeFileName(h.StudentName);
        var path = Path.Combine(folder, $"FeeStatement-{safeName}-{SafeFileName(h.RollNo)}-{DateTime.Now:yyyyMMdd-HHmm}.pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("Queen International School").FontSize(16).Bold();
                    col.Item().Text("Student Fee Statement (complete history)").FontSize(12);
                    col.Item().Text($"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingBottom(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Text($"Student: {h.StudentName}     Class: {h.ClassName}     Roll No: {h.RollNo}");
                    col.Item().Text($"Father: {h.FatherName}     Phone: {h.FatherPhone}");

                    col.Item().PaddingVertical(8).Row(row =>
                    {
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6)
                            .Text(t => { t.Span("Total billed\n").FontSize(9); t.Span($"Rs {Money(h.TotalAmount)}").Bold(); });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6)
                            .Text(t => { t.Span("Total paid\n").FontSize(9); t.Span($"Rs {Money(h.TotalPaid)}").Bold(); });
                        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6)
                            .Text(t => { t.Span("Balance due\n").FontSize(9); t.Span($"Rs {Money(h.TotalBalance)}").Bold().FontColor(h.TotalBalance > 0 ? Colors.Red.Darken2 : Colors.Green.Darken2); });
                    });

                    col.Item().Text("1. Fee-wise status (kon si fee, kon si paid, kon si baaki)").Bold().FontSize(11);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.2f); // Month
                            c.RelativeColumn(2.4f); // Fee
                            c.RelativeColumn(1.4f); // Due
                            c.RelativeColumn(1.4f); // Amount
                            c.RelativeColumn(1.4f); // Paid
                            c.RelativeColumn(1.4f); // Balance
                            c.RelativeColumn(1.3f); // Status
                        });
                        table.Header(header =>
                        {
                            foreach (var title in new[] { "Month", "Fee", "Due date", "Amount", "Paid", "Balance", "Status" })
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(title).Bold();
                        });
                        foreach (var f in h.Fees)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(f.Month);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(f.FeeHead);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(f.DueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Money(f.Amount));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Money(f.AmountPaid));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Money(f.Balance));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(f.Status);
                        }
                    });

                    col.Item().PaddingTop(10).Text("2. Payment history (date & time, every payment)").Bold().FontSize(11);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2.2f); // Date & time
                            c.RelativeColumn(1.2f); // Month
                            c.RelativeColumn(2.4f); // Fee
                            c.RelativeColumn(1.4f); // Amount
                            c.RelativeColumn(2.2f); // Receipt
                            c.RelativeColumn(1.3f); // Method
                        });
                        table.Header(header =>
                        {
                            foreach (var title in new[] { "Date & time", "Month", "Fee", "Amount", "Receipt", "Method" })
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(title).Bold();
                        });
                        if (h.Payments.Count == 0)
                        {
                            table.Cell().ColumnSpan(6).Padding(6).Text("No payments recorded yet.").Italic();
                        }
                        foreach (var p in h.Payments)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(p.PaidAtPkt.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(p.Month);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(p.FeeHead);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Money(p.Amount));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(p.ReceiptNo);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(p.Method ?? "-");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(path);

        return path;
    }

    /// <summary>Plain-text summary that is pasted into the WhatsApp chat together with the PDF.</summary>
    public static string BuildWhatsAppMessage(StudentFeeHistoryDto h)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Assalam o Alaikum,");
        sb.AppendLine($"{h.StudentName} (Class {h.ClassName}, Roll {h.RollNo}) ki fee ka mukammal record:");
        sb.AppendLine($"Total: Rs {Money(h.TotalAmount)}");
        sb.AppendLine($"Paid: Rs {Money(h.TotalPaid)}");
        sb.AppendLine($"Balance: Rs {Money(h.TotalBalance)}");

        var unpaid = h.Fees.Where(f => f.Balance > 0).ToList();
        if (unpaid.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Baqi fees:");
            foreach (var f in unpaid)
                sb.AppendLine($"- {f.Month} {f.FeeHead}: Rs {Money(f.Balance)} (due {f.DueDate:dd MMM yyyy})");
        }

        sb.AppendLine();
        sb.AppendLine("Detailed PDF statement attached.");
        sb.Append("Shukriya - Queen International School");
        return sb.ToString();
    }

    /// <summary>Converts a Pakistani mobile number to the 92XXXXXXXXXX form used by wa.me. Null if invalid.</summary>
    public static string? WhatsAppNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith("0")) digits = "92" + digits[1..];
        else if (digits.Length == 10 && digits.StartsWith("3")) digits = "92" + digits;
        return digits.Length == 12 && digits.StartsWith("92") ? digits : null;
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "student" : cleaned;
    }
}
