import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';

class FeesProvider extends ChangeNotifier {
  final ApiClient api;
  FeesProvider(this.api);

  List<FeeRow> rows = [];
  bool loading = false;
  String? error;

  List<FeeRow> get pending => rows.where((r) => !r.isPaid).toList()
    ..sort((a, b) => a.dueDate.compareTo(b.dueDate));

  List<FeeRow> get paid => rows.where((r) => r.isPaid).toList()
    ..sort((a, b) => b.month.compareTo(a.month));

  // CHANGED: totals reflect outstanding balances, not original invoice values.
  num get totalDue => pending.fold<num>(0, (sum, r) => sum + r.balance);

  Future<List<FeePaymentRow>> paymentHistory(int feeId) => api.getFeePaymentHistory(feeId);

  Future<void> refresh() async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      rows = await api.getMyFees();
    } on ApiException catch (e) {
      error = e.message;
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<ReceiptData> receiptFor(int feeId) => api.getReceipt(feeId);
}
