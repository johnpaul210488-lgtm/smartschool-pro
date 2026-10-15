import 'package:pdf/pdf.dart';
import 'package:pdf/widgets.dart' as pw;
import 'package:printing/printing.dart';

import 'models.dart';

/// Builds the fee-receipt PDF on-device (works offline once the data is loaded),
/// then opens the share sheet (Save as PDF / WhatsApp / Drive).
class ReceiptPdf {
  static Future<void> share(ReceiptData r) async {
    final doc = pw.Document();

    pw.Widget row(String label, String value, {bool bold = false}) => pw.Padding(
          padding: const pw.EdgeInsets.symmetric(vertical: 3),
          child: pw.Row(
            mainAxisAlignment: pw.MainAxisAlignment.spaceBetween,
            children: [
              pw.Text(label, style: const pw.TextStyle(fontSize: 11, color: PdfColors.grey700)),
              pw.Text(value,
                  style: pw.TextStyle(
                      fontSize: 11,
                      fontWeight: bold ? pw.FontWeight.bold : pw.FontWeight.normal)),
            ],
          ),
        );

    doc.addPage(
      pw.Page(
        pageFormat: PdfPageFormat.a5,
        build: (context) => pw.Column(
          crossAxisAlignment: pw.CrossAxisAlignment.stretch,
          children: [
            pw.Container(
              padding: const pw.EdgeInsets.all(14),
              decoration: pw.BoxDecoration(
                color: PdfColors.blue900,
                borderRadius: pw.BorderRadius.circular(8),
              ),
              child: pw.Column(
                children: [
                  pw.Text('SmartSchool Pro',
                      style: pw.TextStyle(color: PdfColors.white, fontSize: 18, fontWeight: pw.FontWeight.bold)),
                  pw.SizedBox(height: 4),
                  pw.Text('OFFICIAL FEE RECEIPT',
                      style: const pw.TextStyle(color: PdfColors.white, fontSize: 10)),
                ],
              ),
            ),
            pw.SizedBox(height: 16),
            row('Receipt No', r.receiptNo, bold: true),
            row('Student', r.studentName),
            row('Class', r.className),
            row('Fee Month', r.month),
            row('Due Date', r.dueDate),
            row('Paid On', r.paymentDatePakistan),
            if (r.notes != null && r.notes!.isNotEmpty) row('Notes', r.notes!),
            pw.Divider(height: 24),
            pw.Container(
              padding: const pw.EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              decoration: pw.BoxDecoration(
                color: PdfColors.green50,
                borderRadius: pw.BorderRadius.circular(6),
              ),
              child: pw.Row(
                mainAxisAlignment: pw.MainAxisAlignment.spaceBetween,
                children: [
                  pw.Text('Amount Received',
                      style: pw.TextStyle(fontSize: 13, fontWeight: pw.FontWeight.bold)),
                  pw.Text('Rs. ${r.amount}',
                      style: pw.TextStyle(
                          fontSize: 15, fontWeight: pw.FontWeight.bold, color: PdfColors.green800)),
                ],
              ),
            ),
            pw.Spacer(),
            pw.Center(
              child: pw.Text('PAID ✔  |  This is a system-generated receipt.',
                  style: const pw.TextStyle(fontSize: 9, color: PdfColors.grey600)),
            ),
          ],
        ),
      ),
    );

    await Printing.sharePdf(
      bytes: await doc.save(),
      filename: 'receipt-${r.receiptNo}.pdf',
    );
  }
}
