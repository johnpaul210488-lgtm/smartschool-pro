import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/models.dart';
import '../core/receipt_pdf.dart';
import '../providers/fees_provider.dart';
import '../providers/locale_provider.dart';

class FeesScreen extends StatefulWidget {
  const FeesScreen({super.key});

  @override
  State<FeesScreen> createState() => _FeesScreenState();
}

class _FeesScreenState extends State<FeesScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) =>
        context.read<FeesProvider>().refresh());
  }

  Future<void> _openReceipt(FeeRow row) async {
    if (row.receiptNo == null) return;
    try {
      final receipt = await context.read<FeesProvider>().receiptFor(row.id);
      await ReceiptPdf.share(receipt);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Receipt: $e')));
      }
    }
  }

  // CHANGED: show receipt-level history, including partial payments.
  Future<void> _openPaymentHistory(FeeRow row) async {
    final future = context.read<FeesProvider>().paymentHistory(row.id);
    await showDialog<void>(context: context, builder: (context) => AlertDialog(
      title: Text('Payment history — ${row.studentName}'),
      content: SizedBox(width: 420, child: FutureBuilder<List<FeePaymentRow>>(
        future: future,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (snapshot.hasError) return Text('Could not load payment history: ${snapshot.error}');
          final rows = snapshot.data ?? const <FeePaymentRow>[];
          if (rows.isEmpty) return const Text('No payments have been recorded for this fee yet.');
          return ListView(shrinkWrap: true, children: rows.map((p) => ListTile(
            dense: true,
            title: Text('Rs ${p.amount} — ${p.method ?? 'Payment'}'),
            subtitle: Text('${p.paidDate} • ${p.receiptNo}'),
          )).toList());
        },
      )),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Close'))],
    ));
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final fees = context.watch<FeesProvider>();

    return Scaffold(
      appBar: AppBar(title: Text(loc.t('fees'))),
      body: RefreshIndicator(
        onRefresh: () => context.read<FeesProvider>().refresh(),
        child: fees.loading && fees.rows.isEmpty
            ? const Center(child: CircularProgressIndicator())
            : ListView(
                padding: const EdgeInsets.all(12),
                children: [
                  // Total due banner.
                  Card(
                    color: fees.totalDue > 0 ? Colors.red.shade700 : Colors.green.shade700,
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Row(
                        children: [
                          Icon(fees.totalDue > 0 ? Icons.warning_amber : Icons.check_circle,
                              color: Colors.white),
                          const SizedBox(width: 10),
                          Text('${loc.t('total_due')}:  ${loc.t('rs')} ${fees.totalDue}',
                              style: const TextStyle(
                                  color: Colors.white,
                                  fontSize: 18,
                                  fontWeight: FontWeight.bold)),
                        ],
                      ),
                    ),
                  ),

                  const SizedBox(height: 12),
                  if (fees.pending.isNotEmpty)
                    _SectionTitle(text: loc.t('pending_dues')),
                  ...fees.pending.map((r) => _FeeTile(row: r, pending: true, loc: loc, onHistory: _openPaymentHistory)),

                  const SizedBox(height: 12),
                  if (fees.paid.isNotEmpty)
                    _SectionTitle(text: loc.t('paid_history')),
                  ...fees.paid.map((r) => _FeeTile(row: r, pending: false, loc: loc, onReceipt: _openReceipt, onHistory: _openPaymentHistory)),

                  if (fees.rows.isEmpty && !fees.loading)
                    Padding(
                      padding: const EdgeInsets.all(40),
                      child: Center(child: Text(loc.t('no_fees'))),
                    ),
                ],
              ),
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.text});
  final String text;
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 6, left: 4),
        child: Text(text, style: Theme.of(context).textTheme.titleMedium),
      );
}

class _FeeTile extends StatelessWidget {
  const _FeeTile({required this.row, required this.pending, required this.loc, this.onReceipt, this.onHistory});
  final FeeRow row;
  final bool pending;
  final LocaleProvider loc;
  final Future<void> Function(FeeRow)? onReceipt;
  final Future<void> Function(FeeRow)? onHistory;

  @override
  Widget build(BuildContext context) {
    final isOverdue = row.status == 'Overdue';
    return Card(
      // RED highlight for pending/overdue dues (spec).
      color: pending ? (isOverdue ? Colors.red.shade50 : Colors.orange.shade50) : null,
      shape: pending
          ? RoundedRectangleBorder(
              side: BorderSide(color: isOverdue ? Colors.red : Colors.orange, width: 1.5),
              borderRadius: BorderRadius.circular(12))
          : null,
      child: ListTile(
        leading: CircleAvatar(
          backgroundColor: pending ? (isOverdue ? Colors.red : Colors.orange) : Colors.green,
          child: Icon(pending ? Icons.schedule : Icons.check, color: Colors.white),
        ),
        onTap: onHistory == null ? null : () => onHistory!(row),
        title: Text('${row.studentName}  —  ${row.feeHeadName ?? 'Fee'} (${row.month})',
            style: const TextStyle(fontWeight: FontWeight.w600)),
        subtitle: Text(pending
            ? '${loc.t('due_on')}: ${row.dueDate}  •  Invoice Rs ${row.amount}  •  Paid Rs ${row.amountPaid}'
            : '${loc.t('paid_on')}: ${row.paidDate ?? ''}  •  ${row.receiptNo ?? ''}'),
        trailing: pending
            ? Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text('${loc.t('rs')} ${row.balance}',
                      style: TextStyle(
                          fontWeight: FontWeight.bold,
                          fontSize: 16,
                          color: isOverdue ? Colors.red : Colors.orange.shade900)),
                  if (isOverdue)
                    Text(loc.t('overdue'),
                        style: const TextStyle(
                            color: Colors.red, fontSize: 11, fontWeight: FontWeight.bold)),
                  if (!isOverdue && row.amountPaid > 0)
                    const Text('Partially paid', style: TextStyle(color: Colors.deepOrange, fontSize: 11)), // CHANGED
                ],
              )
            : row.receiptNo != null
                ? TextButton.icon(
                    onPressed: onReceipt == null ? null : () => onReceipt!(row),
                    icon: const Icon(Icons.picture_as_pdf, size: 18),
                    label: Text(loc.t('receipt')),
                  )
                : Text('${loc.t('rs')} ${row.amount}'),
      ),
    );
  }
}
