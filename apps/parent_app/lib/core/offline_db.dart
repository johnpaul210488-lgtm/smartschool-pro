import 'dart:async';

import 'package:path/path.dart';
import 'package:sqflite/sqflite.dart';

/// Local SQLite cache for the parent app so fee dues, alerts and
/// announcements remain visible when the server is offline.
class OfflineDb {
  OfflineDb._();
  static final OfflineDb instance = OfflineDb._();

  Database? _db;

  Future<Database> get db async => _db ??= await _init();

  Future<Database> _init() async {
    final dbPath = await getDatabasesPath();
    final path = join(dbPath, 'smartschool_parent.db');
    return await openDatabase(
      path,
      version: 1,
      onCreate: (db, version) async {
        // Cached fee rows (parent's children fees).
        await db.execute('''
          CREATE TABLE fee_rows (
            id INTEGER PRIMARY KEY,
            studentId INTEGER NOT NULL,
            studentName TEXT NOT NULL,
            className TEXT NOT NULL,
            month TEXT NOT NULL,
            amount REAL NOT NULL,
            balance REAL NOT NULL,
            dueDate TEXT NOT NULL,
            status TEXT NOT NULL,
            receiptNo TEXT,
            amountPaid REAL DEFAULT 0,
            feeHeadName TEXT,
            updatedAt INTEGER NOT NULL
          )
        ''');

        // Cached pick/drop alert events.
        await db.execute('''
          CREATE TABLE alert_items (
            id INTEGER PRIMARY KEY,
            studentId INTEGER NOT NULL,
            studentName TEXT NOT NULL,
            className TEXT NOT NULL,
            busNumber TEXT NOT NULL,
            eventType TEXT NOT NULL,
            timePakistan TEXT NOT NULL,
            location TEXT,
            updatedAt INTEGER NOT NULL
          )
        ''');

        // Cached parent children cards (so the home screen opens offline).
        await db.execute('''
          CREATE TABLE children_cache (
            id INTEGER PRIMARY KEY,
            studentId INTEGER NOT NULL,
            fullName TEXT NOT NULL,
            photoUrl TEXT,
            className TEXT NOT NULL,
            busId INTEGER,
            busNumber TEXT,
            todayStatus TEXT NOT NULL,
            lastEventText TEXT,
            lastEventTimePakistan TEXT,
            updatedAt INTEGER NOT NULL
          )
        ''');

        // Cached school announcements.
        await db.execute('''
          CREATE TABLE announcements (
            id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            createdByName TEXT,
            timePakistan TEXT NOT NULL,
            updatedAt INTEGER NOT NULL
          )
        ''');

        // Outbox queue for actions that failed while offline.
        await db.execute('''
          CREATE TABLE outbox (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            action TEXT NOT NULL,
            path TEXT NOT NULL,
            body TEXT NOT NULL,
            createdAt INTEGER NOT NULL,
            attempts INTEGER DEFAULT 0
          )
        ''');
      },
    );
  }

  // ---------- Fees cache ----------

  Future<void> cacheFees(List<Map<String, dynamic>> rows) async {
    final db = await this.db;
    await db.delete('fee_rows');
    final now = DateTime.now().millisecondsSinceEpoch;
    for (final r in rows) {
      await db.insert('fee_rows', {
        'id': r['id'],
        'studentId': r['studentId'],
        'studentName': r['studentName'],
        'className': r['className'],
        'month': r['month'],
        'amount': r['amount'],
        'balance': r['balance'],
        'dueDate': r['dueDate'],
        'status': r['status'],
        'receiptNo': r['receiptNo'],
        'amountPaid': r['amountPaid'] ?? 0,
        'feeHeadName': r['feeHeadName'],
        'updatedAt': now,
      });
    }
  }

  Future<List<Map<String, dynamic>>> getCachedFees() async {
    final db = await this.db;
    return await db.query('fee_rows', orderBy: 'dueDate DESC');
  }

  // ---------- Alerts cache ----------

  Future<void> cacheAlerts(List<Map<String, dynamic>> rows) async {
    final db = await this.db;
    await db.delete('alert_items');
    final now = DateTime.now().millisecondsSinceEpoch;
    for (final r in rows) {
      await db.insert('alert_items', {
        'id': r['id'],
        'studentId': r['studentId'],
        'studentName': r['studentName'],
        'className': r['className'],
        'busNumber': r['busNumber'],
        'eventType': r['eventType'],
        'timePakistan': r['timePakistan'],
        'location': r['location'],
        'updatedAt': now,
      });
    }
  }

  Future<List<Map<String, dynamic>>> getCachedAlerts() async {
    final db = await this.db;
    return await db.query('alert_items', orderBy: 'id DESC');
  }

  // ---------- Announcements cache ----------

  Future<void> cacheAnnouncements(List<Map<String, dynamic>> rows) async {
    final db = await this.db;
    await db.delete('announcements');
    final now = DateTime.now().millisecondsSinceEpoch;
    for (final r in rows) {
      await db.insert('announcements', {
        'id': r['id'],
        'title': r['title'],
        'body': r['body'],
        'createdByName': r['createdByName'],
        'timePakistan': r['timePakistan'],
        'updatedAt': now,
      });
    }
  }

  Future<List<Map<String, dynamic>>> getCachedAnnouncements() async {
    final db = await this.db;
    return await db.query('announcements', orderBy: 'id DESC');
  }

  // ---------- Children cache ----------

  Future<void> cacheChildren(List<Map<String, dynamic>> rows) async {
    final db = await this.db;
    await db.delete('children_cache');
    final now = DateTime.now().millisecondsSinceEpoch;
    for (final r in rows) {
      await db.insert('children_cache', {
        'id': r['id'],
        'studentId': r['studentId'],
        'fullName': r['fullName'],
        'photoUrl': r['photoUrl'],
        'className': r['className'],
        'busId': r['busId'],
        'busNumber': r['busNumber'],
        'todayStatus': r['todayStatus'],
        'lastEventText': r['lastEventText'],
        'lastEventTimePakistan': r['lastEventTimePakistan'],
        'updatedAt': now,
      });
    }
  }

  Future<List<Map<String, dynamic>>> getCachedChildren() async {
    final db = await this.db;
    return await db.query('children_cache', orderBy: 'studentId');
  }

  // ---------- Outbox ----------

  Future<void> enqueueOutbox(
      String action, String path, Map<String, dynamic> body) async {
    final db = await this.db;
    await db.insert('outbox', {
      'action': action,
      'path': path,
      'body': body.toString(),
      'createdAt': DateTime.now().millisecondsSinceEpoch,
      'attempts': 0,
    });
  }

  Future<List<Map<String, dynamic>>> getPendingOutbox() async {
    final db = await this.db;
    return await db.query(
      'outbox',
      orderBy: 'createdAt ASC',
      where: 'attempts < 5',
    );
  }

  Future<void> removeOutbox(int id) async {
    final db = await this.db;
    await db.delete('outbox', where: 'id = ?', whereArgs: [id]);
  }

  Future<void> bumpOutboxAttempts(int id) async {
    final db = await this.db;
    await db.update(
      'outbox',
      {'attempts': "attempts + 1"},
      where: 'id = ?',
      whereArgs: [id],
    );
  }

  Future<int> outboxCount() async {
    final db = await this.db;
    final result = await db.rawQuery(
      'SELECT COUNT(*) as c FROM outbox WHERE attempts < 5');
    return Sqflite.firstIntValue(result) ?? 0;
  }
}
