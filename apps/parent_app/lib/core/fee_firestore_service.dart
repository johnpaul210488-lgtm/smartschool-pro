import 'package:cloud_firestore/cloud_firestore.dart';

import 'models.dart';

/// Reads/writes fee rows to Firebase Firestore so parents can see their
/// children's dues even when the school server is offline.
///
/// Collection: `school_fees` — documents keyed by fee id.
/// Each doc: { studentId, studentName, className, month, amount, amountPaid,
/// balance, dueDate, status, receiptNo, feeHeadName, updatedAt }
class FeeFirestoreService {
  FeeFirestoreService._();
  static final FeeFirestoreService instance = FeeFirestoreService._();

  final FirebaseFirestore _db = FirebaseFirestore.instance;

  /// Collection reference for fee rows.
  CollectionReference get _fees => _db.collection('school_fees');

  /// Write (upsert) a list of fee rows to Firestore.
  Future<void> writeFees(List<FeeRow> rows) async {
    final batch = _db.batch();
    for (final r in rows) {
      final ref = _fees.doc(r.id.toString());
      batch.set(ref, {
        'id': r.id,
        'studentId': r.studentId,
        'studentName': r.studentName,
        'className': r.className,
        'amount': r.amount,
        'amountPaid': r.amountPaid,
        'balance': r.balance,
        'dueDate': r.dueDate,
        'status': r.status,
        'receiptNo': r.receiptNo,
        'feeHeadName': r.feeHeadName,
        'updatedAt': FieldValue.serverTimestamp(),
      }, SetOptions(merge: true));
    }
    await batch.commit();
  }

  /// Read all fee rows for a given student from Firestore.
  Future<List<FeeRow>> readFeesForStudent(int studentId) async {
    final snap = await _fees
        .where('studentId', isEqualTo: studentId)
        .get();
    return snap.docs
        .map((d) => _docToFeeRow(d.data(), d.id))
        .where((r) => r != null)
        .cast<FeeRow>()
        .toList();
  }

  /// Read all fee rows (all students) from Firestore — used after login
  /// when the parent's children are known.
  Future<List<FeeRow>> readAllFees() async {
    final snap = await _fees.get();
    return snap.docs
        .map((d) => _docToFeeRow(d.data(), d.id))
        .where((r) => r != null)
        .cast<FeeRow>()
        .toList();
  }

  FeeRow? _docToFeeRow(Map<String, dynamic>? data, String docId) {
    if (data == null) return null;
    try {
      final amount = (data['amount'] ?? 0) as num;
      final paid = (data['amountPaid'] ?? 0) as num;
      return FeeRow(
        id: int.tryParse(docId) ?? (data['id'] as int? ?? 0),
        studentId: data['studentId'] as int? ?? 0,
        studentName: data['studentName'] as String? ?? '',
        className: data['className'] as String? ?? '',
        amount: amount,
        amountPaid: paid,
        balance: (data['balance'] as num?) ?? (amount - paid),
        feeHeadName: data['feeHeadName'] as String?,
        month: data['month'] as String? ?? '',
        dueDate: data['dueDate'] as String? ?? '',
        paidDate: data['paidDate'] as String?,
        status: data['status'] as String? ?? 'Pending',
        receiptNo: data['receiptNo'] as String?,
      );
    } catch (_) {
      return null;
    }
  }
}
