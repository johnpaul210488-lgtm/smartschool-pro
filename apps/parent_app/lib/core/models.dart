// Data models matching the API JSON (camelCase). Keep in sync with
// src/SmartSchool.Core/DTOs when endpoints change.

class ChildCard {
  ChildCard({
    required this.studentId,
    required this.fullName,
    this.photoUrl,
    required this.className,
    this.busId,
    this.busNumber,
    required this.todayStatus,
    this.lastEventText,
    this.lastEventTimePakistan,
  });

  final int studentId;
  final String fullName;
  final String? photoUrl;
  final String className;
  final int? busId;
  final String? busNumber;

  /// NotBoarded | OnBus | AtSchool | DroppedHome
  final String todayStatus;
  final String? lastEventText;
  final String? lastEventTimePakistan;

  factory ChildCard.fromJson(Map<String, dynamic> j) => ChildCard(
        studentId: j['studentId'] as int,
        fullName: j['fullName'] as String? ?? '',
        photoUrl: j['photoUrl'] as String?,
        className: j['className'] as String? ?? '',
        busId: j['busId'] as int?,
        busNumber: j['busNumber'] as String?,
        todayStatus: j['todayStatus'] as String? ?? 'NotBoarded',
        lastEventText: j['lastEventText'] as String?,
        lastEventTimePakistan: j['lastEventTimePakistan'] as String?,
      );
}

class ParentSettings {
  ParentSettings({
    required this.username,
    this.phone,
    required this.preferredLanguage,
    required this.receiveWhatsApp,
  });

  final String username;
  final String? phone;
  final String preferredLanguage;
  final bool receiveWhatsApp;

  factory ParentSettings.fromJson(Map<String, dynamic> j) => ParentSettings(
        username: j['username'] as String? ?? '',
        phone: j['phone'] as String?,
        preferredLanguage: j['preferredLanguage'] as String? ?? 'en',
        receiveWhatsApp: j['receiveWhatsApp'] as bool? ?? true,
      );
}

class FeeRow {
  FeeRow({
    required this.id,
    required this.studentId,
    required this.studentName,
    required this.className,
    required this.amount,
    required this.month,
    required this.dueDate,
    this.paidDate,
    required this.status,
    this.receiptNo,
    this.notes,
  });

  final int id;
  final int studentId;
  final String studentName;
  final String className;
  final num amount;
  final String month;
  final String dueDate;
  final String? paidDate;

  /// Pending | Paid | Overdue
  final String status;
  final String? receiptNo;
  final String? notes;

  bool get isPaid => status == 'Paid';

  factory FeeRow.fromJson(Map<String, dynamic> j) => FeeRow(
        id: j['id'] as int,
        studentId: j['studentId'] as int,
        studentName: j['studentName'] as String? ?? '',
        className: j['className'] as String? ?? '',
        amount: j['amount'] as num? ?? 0,
        month: j['month'] as String? ?? '',
        dueDate: j['dueDate'] as String? ?? '',
        paidDate: j['paidDate'] as String?,
        status: j['status'] as String? ?? 'Pending',
        receiptNo: j['receiptNo'] as String?,
        notes: j['notes'] as String?,
      );
}

class ReceiptData {
  ReceiptData({
    required this.receiptNo,
    required this.studentName,
    required this.className,
    required this.month,
    required this.amount,
    required this.dueDate,
    required this.paymentDatePakistan,
    this.notes,
  });

  final String receiptNo;
  final String studentName;
  final String className;
  final String month;
  final num amount;
  final String dueDate;
  final String paymentDatePakistan;
  final String? notes;

  factory ReceiptData.fromJson(Map<String, dynamic> j) => ReceiptData(
        receiptNo: j['receiptNo'] as String? ?? '',
        studentName: j['studentName'] as String? ?? '',
        className: j['className'] as String? ?? '',
        month: j['month'] as String? ?? '',
        amount: j['amount'] as num? ?? 0,
        dueDate: j['dueDate'] as String? ?? '',
        paymentDatePakistan: j['paymentDatePakistan'] as String? ?? '',
        notes: j['notes'] as String?,
      );
}

class AnnouncementItem {
  AnnouncementItem({
    required this.id,
    required this.title,
    required this.body,
    this.createdByName,
    required this.timePakistan,
  });

  final int id;
  final String title;
  final String body;
  final String? createdByName;
  final String timePakistan;

  factory AnnouncementItem.fromJson(Map<String, dynamic> j) => AnnouncementItem(
        id: j['id'] as int,
        title: j['title'] as String? ?? '',
        body: j['body'] as String? ?? '',
        createdByName: j['createdByName'] as String?,
        timePakistan: j['timePakistan'] as String? ?? '',
      );
}

class AlertItem {
  AlertItem({
    required this.id,
    required this.studentId,
    required this.studentName,
    required this.eventType,
    required this.timePakistan,
    this.location,
  });

  final int id;
  final int studentId;
  final String studentName;

  /// PickIn | DropOut
  final String eventType;
  final String timePakistan;
  final String? location;

  factory AlertItem.fromJson(Map<String, dynamic> j) => AlertItem(
        id: (j['id'] as num).toInt(),
        studentId: j['studentId'] as int,
        studentName: j['studentName'] as String? ?? '',
        eventType: j['type'] as String? ?? '',
        timePakistan: j['timePakistan'] as String? ?? '',
        location: j['location'] as String?,
      );
}

class BusLocation {
  BusLocation({
    required this.busId,
    required this.latitude,
    required this.longitude,
    required this.timePakistan,
  });

  final int busId;
  final double latitude;
  final double longitude;
  final String timePakistan;

  factory BusLocation.fromJson(Map<String, dynamic> j) => BusLocation(
        busId: j['busId'] as int,
        latitude: (j['latitude'] as num).toDouble(),
        longitude: (j['longitude'] as num).toDouble(),
        timePakistan: j['timePakistan'] as String? ?? '',
      );
}
