using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>Shows the fee receipt preview + prints it via FlowDocument.</summary>
public partial class FeeReceiptViewModel : ObservableObject
{
    private readonly FeeListItem _fee;

    [ObservableProperty] private string _receiptNo = "-";
    [ObservableProperty] private string _paidDateText = "-";

    public event Action? BackRequested;

    public FeeListItem Fee => _fee;

    public FeeReceiptViewModel(FeeListItem fee)
    {
        _fee = fee;
        _receiptNo = fee.ReceiptNo ?? $"RCP-{fee.Id:D6}";
        _paidDateText = (fee.PaidDate ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd MMM yyyy");
    }

    /// <summary>Builds the printable FlowDocument (also used by the on-screen preview host).</summary>
    public FlowDocument BuildReceiptDocument()
    {
        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C4DFF"));
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(40),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            ColumnWidth = 450
        };

        var school = new Paragraph(new Run("🏫 Queen International School"))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            TextAlignment = TextAlignment.Center
        };
        doc.Blocks.Add(school);
        doc.Blocks.Add(new Paragraph(new Run("Official Fee Receipt"))
        {
            FontSize = 15,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        });

        void Row(string label, string value) => doc.Blocks.Add(new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 4),
            Inlines =
            {
                new Run($"{label}:  ") { FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray },
                new Run(value)
            }
        });

        Row("Receipt No", ReceiptNo);
        Row("Date", PaidDateText);
        Row("Student", _fee.StudentName);
        Row("Class", _fee.ClassName);
        Row("Fee Month", _fee.Month);
        Row("Invoice amount", $"Rs {_fee.Amount:N2}");
        Row("This payment", $"Rs {(_fee.LastPaymentAmount > 0m ? _fee.LastPaymentAmount : _fee.Amount):N2}");
        Row("Balance remaining", $"Rs {_fee.Balance:N2}");
        Row("Status", _fee.Status);

        doc.Blocks.Add(new Paragraph(new Run("─".PadRight(48, '─'))) { Margin = new Thickness(0, 16, 0, 16) });
        doc.Blocks.Add(new Paragraph(new Run("Received by: ______________________        Parent signature: ______________________"))
        { FontSize = 12 });
        doc.Blocks.Add(new Paragraph(new Run("Queen International School — automatic receipt. Kisi ghalti ki sorat mein office se rabta karein."))
        { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 24, 0, 0), TextAlignment = TextAlignment.Center });

        return doc;
    }

    [RelayCommand]
    private void Print()
    {
        var doc = BuildReceiptDocument();
        var pd = new PrintDialog();
        if (pd.ShowDialog() == true)
        {
            doc.PageHeight = pd.PrintableAreaHeight;
            doc.PageWidth = pd.PrintableAreaWidth;
            doc.ColumnWidth = pd.PrintableAreaWidth;
            pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,
                $"Fee Receipt {ReceiptNo}");
        }
    }

    [RelayCommand] private void Back() => BackRequested?.Invoke();
}
