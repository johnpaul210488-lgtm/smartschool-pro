/// API models — mirror SmartSchool.API envelope + DTOs (camelCase JSON).

class ApiException implements Exception {
  ApiException(this.message, {this.errors = const []});
  final String message;
  final List<String> errors;
  @override
  String toString() => errors.isNotEmpty ? '$message\n${errors.join('\n')}' : message;
}

/// Wraps { success, message, data, errors }.
class Envelope<T> {
  Envelope({required this.success, required this.message, this.data, this.errors = const []});
  final bool success;
  final String message;
  final T? data;
  final List<String> errors;
}

class LoginResult {
  LoginResult({
    required this.token,
    required this.expiresAtUtc,
    required this.userId,
    required this.username,
    required this.fullName,
    required this.role,
  });

  factory LoginResult.fromJson(Map<String, dynamic> j) => LoginResult(
        token: j['token'] as String,
        expiresAtUtc: DateTime.parse(j['expiresAtUtc'] as String),
        userId: (j['user'] as Map<String, dynamic>)['id'] as int,
        username: j['user']['username'] as String,
        fullName: j['user']['fullName'] as String,
        role: j['user']['role'] as String,
      );

  final String token;
  final DateTime expiresAtUtc;
  final int userId;
  final String username;
  final String fullName;
  final String role;

  Map<String, dynamic> toJson() => {
        'token': token,
        'expiresAtUtc': expiresAtUtc.toIso8601String(),
        'user': {'id': userId, 'username': username, 'fullName': fullName, 'role': role},
      };
}

class Bus {
  Bus({
    required this.id,
    required this.busNumber,
    required this.route,
    required this.vehicleNo,
    required this.capacity,
    required this.studentCount,
  });

  factory Bus.fromJson(Map<String, dynamic> j) => Bus(
        id: j['id'] as int,
        busNumber: j['busNumber'] as String,
        route: j['route'] as String,
        vehicleNo: j['vehicleNo'] as String,
        capacity: j['capacity'] as int,
        studentCount: j['studentCount'] as int,
      );

  final int id;
  final String busNumber;
  final String route;
  final String vehicleNo;
  final int capacity;
  final int studentCount;
}

class Trip {
  Trip({required this.id, required this.busId, required this.busNumber, required this.route});

  factory Trip.fromJson(Map<String, dynamic> j) => Trip(
        id: j['id'] as int,
        busId: j['busId'] as int,
        busNumber: j['busNumber'] as String,
        route: j['route'] as String,
      );

  final int id;
  final int busId;
  final String busNumber;
  final String route;
}

/// One student on the driver manifest — "PickIn" | "DropOut" | null today.
enum PickStatus { none, picked, dropped }

class ManifestStudent {
  ManifestStudent({
    required this.studentId,
    required this.fullName,
    required this.className,
    required this.rollNo,
    required this.fatherName,
    required this.fatherPhone,
    this.photoUrl,
    PickStatus? status,
  }) : status = status ?? PickStatus.none;

  factory ManifestStudent.fromJson(Map<String, dynamic> j) => ManifestStudent(
        studentId: j['studentId'] as int,
        fullName: j['fullName'] as String,
        className: j['className'] as String,
        rollNo: j['rollNo'] as String,
        fatherName: j['fatherName'] as String,
        fatherPhone: j['fatherPhone'] as String,
        photoUrl: j['photoUrl'] as String?,
        status: switch (j['todayStatus'] as String?) {
          'PickIn' => PickStatus.picked,
          'DropOut' => PickStatus.dropped,
          _ => PickStatus.none,
        },
      );

  final int studentId;
  final String fullName;
  final String className;
  final String rollNo;
  final String fatherName;
  final String fatherPhone;
  final String? photoUrl;
  PickStatus status;

  bool get canPickIn => status == PickStatus.none;
  bool get canDropOut => status == PickStatus.picked;
}

/// Offline outbox row: one pick-in/drop-out tap waiting to be replayed.
class PendingEvent {
  PendingEvent({
    this.id,
    required this.studentId,
    required this.busId,
    required this.tripId,
    required this.type, // 'pick-in' | 'drop-out'
    required this.latitude,
    required this.longitude,
    required this.timestampUtc,
    this.attempts = 0,
  });

  final int? id;
  final int studentId;
  final int busId;
  final int tripId;
  final String type;
  final double latitude;
  final double longitude;
  final String timestampUtc; // ISO 8601 — the moment it HAPPENED
  int attempts;

  Map<String, dynamic> toMap() => {
        'id': id,
        'studentId': studentId,
        'busId': busId,
        'tripId': tripId,
        'type': type,
        'latitude': latitude,
        'longitude': longitude,
        'timestampUtc': timestampUtc,
        'attempts': attempts,
      };

  factory PendingEvent.fromMap(Map<String, dynamic> m) => PendingEvent(
        id: m['id'] as int?,
        studentId: m['studentId'] as int,
        busId: m['busId'] as int,
        tripId: m['tripId'] as int,
        type: m['type'] as String,
        latitude: (m['latitude'] as num).toDouble(),
        longitude: (m['longitude'] as num).toDouble(),
        timestampUtc: m['timestampUtc'] as String,
        attempts: m['attempts'] as int,
      );

  Map<String, dynamic> toApiBody() => {
        'studentId': studentId,
        'busId': busId,
        'tripId': tripId,
        'timestampUtc': timestampUtc,
        'latitude': latitude,
        'longitude': longitude,
      };
}
