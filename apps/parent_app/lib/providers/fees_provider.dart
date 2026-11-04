import 'dart:convert';

import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/fee_firestore_service.dart';
import '../core/models.dart';
import '../core/offline_db.dart';

class FeesProvider extends ChangeNotifier {
  final ApiClient api;
  FeesProvider(this.api);

  List<FeeRow> rows = [];
  bool loading = false;
  String? error;
  bool _showingCache = false;

  List<FeeRow> get pending => rows.where((r) => !r.isPaid).toList()
    ..sort((a, b) => a.dueDate.compareTo(b.dueDate));

  List<FeeRow> get paid => rows.where((r) => r.isPaid).toList()
    ..sort((a, b) => b.month.compareTo(a.month));

  // CHANGED: totals reflect outstanding balances, not original invoice values.
  num get totalDue => pending.fold<num>(0, (sum, r) => sum + r.balance);

  /// True when current rows came from local cache (server offline).
  bool get showingCache => _showingCache;

  Future<List<FeePaymentRow>> paymentHistory(int feeId) => api.getFeePaymentHistory(feeId);

  Future<void> refresh() async {
    loading = true;
    error = null;
    _showingCache = false;
    notifyListeners();

    // Strategy: cloud (Firestore) → school server API → local SQLite cache.
    // This keeps fees visible even when the school server is off, because
    // Firestore is a separate cloud service.
    try {
      rows = await FeeFirestoreService.instance.readAllFees();
      if (rows.isNotEmpty) {
        // Also mirror to SQLite for when Firestore is unreachable too.
        await OfflineDb.instance.cacheFees(
          rows.map((r) => {
            'id': r.id,
            'studentId': r.studentId,
            'studentName': r.studentName,
            'className': r.className,
            'month': r.month,
            'amount': r.amount,
            'balance': r.balance,
            'dueDate': r.dueDate,
            'status': r.status,
            'receiptNo': r.receiptNo,
            'amountPaid': r.amountPaid,
            'feeHeadName': r.feeHeadName,
          }).toList(),
        );
        _showingCache = false;
        loading = false;
        notifyListeners();
        return;
      }
    } catch (e) {
      // Firestore unavailable (no config / network) — fall through to API.
      debugPrint('[Fees] Firestore read failed: $e');
    }

    // Try the school server API.
    try {
      rows = await api.getMyFees();
      // Cache to SQLite for offline use.
      await OfflineDb.instance.cacheFees(rows.map((r) => {
            'id': r.id,
            'studentId': r.studentId,
            'studentName': r.studentName,
            'className': r.className,
            'month': r.month,
            'amount': r.amount,
            'balance': r.balance,
            'dueDate': r.dueDate,
            'status': r.status,
            'receiptNo': r.receiptNo,
            'amountPaid': r.amountPaid,
            'feeHeadName': r.feeHeadName,
          }).toList());
      // Also push to Firestore so it's available from the cloud next time.
      unawaited(FeeFirestoreService.instance.writeFees(rows));
      _showingCache = false;
    } on ApiException catch (e) {
      error = e.message;
      // Fall back to local cache so the screen is not blank when offline.
      final cached = await OfflineDb.instance.getCachedFees();
      if (cached.isNotEmpty) {
        rows = cached.map((m) => FeeRow.fromJson(_decodeMap(m))).toList();
        _showingCache = true;
        error = '${error}\\n(Server offline — dikhaya ja raha hai purana data)';
      } else {
        rows = [];
        _showingCache = false;
      }
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Map<String, dynamic> _decodeMap(Map<String, dynamic> m) {
    // sqflite stores everything as dynamic; convert types back.
    return {
      'id': m['id'] as int,
      'studentId': m['studentId'] as int,
      'studentName': m['studentName'] as String,
      'className': m['className'] as String,
      'month': m['month'] as String,
      'amount': (m['amount'] as num).toDouble(),
      'balance': (m['balance'] as num).toDouble(),
      'dueDate': m['dueDate'] as String,
      'status': m['status'] as String,
      'receiptNo': m['receiptNo'] as String?,
      'amountPaid': (m['amountPaid'] as num).toDouble(),
      'feeHeadName': m['feeHeadName'] as String?,
    };
  }

  Future<ReceiptData> receiptFor(int feeId) => api.getReceipt(feeId);
}
