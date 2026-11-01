import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

import '../models/models.dart';

/// SQLite-backed FIFO outbox for pick-in/drop-out taps and GPS locations
/// while offline. The API trusts each event's own device timestamp, so
/// parents' WhatsApp messages stay accurate even after a delayed sync.
class OfflineOutbox {
  OfflineOutbox._();
  static final OfflineOutbox instance = OfflineOutbox._();

  static const _dbName = 'smartschool_outbox.db';
  static const _eventTable = 'pending_events';
  static const _gpsTable = 'pending_gps';

  Database? _db;

  Future<Database> get _database async {
    _db ??= await openDatabase(
      p.join(await getDatabasesPath(), _dbName),
      version: 2,
      onCreate: (db, _) async {
        await db.execute('''\
CREATE TABLE $_eventTable(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  studentId INTEGER NOT NULL,
  busId INTEGER NOT NULL,
  tripId INTEGER NOT NULL,
  type TEXT NOT NULL,
  latitude REAL NOT NULL,
  longitude REAL NOT NULL,
  timestampUtc TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
)''');
        await db.execute('''\
CREATE TABLE $_gpsTable(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  tripId INTEGER NOT NULL,
  latitude REAL NOT NULL,
  longitude REAL NOT NULL,
  speedKmh REAL NOT NULL,
  recordedAt TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
)''');
      },
      onUpgrade: (db, oldVersion, newVersion) async {
        if (oldVersion < 2) {
          await db.execute('''\
CREATE TABLE $_gpsTable(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  tripId INTEGER NOT NULL,
  latitude REAL NOT NULL,
  longitude REAL NOT NULL,
  speedKmh REAL NOT NULL,
  recordedAt TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
)''');
        }
      },
    );
    return _db!;
  }

  // ---------- Pick/Drop events ----------

  Future<int> enqueueEvent(PendingEvent e) async {
    final db = await _database;
    return db.insert(_eventTable, e.toMap()..remove('id'));
  }

  Future<List<PendingEvent>> pendingEvents() async {
    final db = await _database;
    final rows = await db.query(_eventTable, orderBy: 'id ASC');
    return rows.map(PendingEvent.fromMap).toList();
  }

  Future<int> pendingEventCount() async {
    final db = await _database;
    final c = await db.rawQuery('SELECT COUNT(*) AS c FROM $_eventTable');
    return (c.first['c'] as int?) ?? 0;
  }

  Future<void> removeEvent(int id) async {
    final db = await _database;
    await db.delete(_eventTable, where: 'id = ?', whereArgs: [id]);
  }

  Future<void> bumpEventAttempts(int id) async {
    final db = await _database;
    await db.rawUpdate('UPDATE $_eventTable SET attempts = attempts + 1 WHERE id = ?', [id]);
  }

  // ---------- GPS locations ----------

  /// Queue a GPS location when the server is unreachable.
  Future<int> enqueueGps({
    required int tripId,
    required double latitude,
    required double longitude,
    required double speedKmh,
    required String recordedAt,
  }) async {
    final db = await _database;
    return db.insert(_gpsTable, {
      'tripId': tripId,
      'latitude': latitude,
      'longitude': longitude,
      'speedKmh': speedKmh,
      'recordedAt': recordedAt,
      'attempts': 0,
    });
  }

  /// Return all pending GPS locations oldest-first.
  Future<List<Map<String, dynamic>>> pendingGps() async {
    final db = await _database;
    return await db.query(_gpsTable, orderBy: 'id ASC');
  }

  Future<int> pendingGpsCount() async {
    final db = await _database;
    final c = await db.rawQuery('SELECT COUNT(*) AS c FROM $_gpsTable');
    return (c.first['c'] as int?) ?? 0;
  }

  Future<void> removeGps(int id) async {
    final db = await _database;
    await db.delete(_gpsTable, where: 'id = ?', whereArgs: [id]);
  }

  Future<void> bumpGpsAttempts(int id) async {
    final db = await _database;
    await db.rawUpdate('UPDATE $_gpsTable SET attempts = attempts + 1 WHERE id = ?', [id]);
  }

  // ---------- Combined ----------

  Future<int> pendingCount() async {
    return await pendingEventCount() + await pendingGpsCount();
  }

  Future<void> clearAll() async {
    final db = await _database;
    await db.delete(_eventTable);
    await db.delete(_gpsTable);
  }
}