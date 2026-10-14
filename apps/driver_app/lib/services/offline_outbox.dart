import 'package:path/path.dart' as p;
import 'package:sqflite/sqflite.dart';

import '../models/models.dart';

/// SQLite-backed FIFO outbox for pick-in/drop-out taps while offline.
/// The API trusts each event's own device timestamp, so parents' WhatsApp
/// messages stay accurate even after a delayed sync.
class OfflineOutbox {
  OfflineOutbox._();
  static final OfflineOutbox instance = OfflineOutbox._();

  static const _dbName = 'smartschool_outbox.db';
  static const _table = 'pending_events';

  Database? _db;

  Future<Database> get _database async {
    _db ??= await openDatabase(
      p.join(await getDatabasesPath(), _dbName),
      version: 1,
      onCreate: (db, _) => db.execute('''
        CREATE TABLE $_table(
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          studentId INTEGER NOT NULL,
          busId INTEGER NOT NULL,
          tripId INTEGER NOT NULL,
          type TEXT NOT NULL,
          latitude REAL NOT NULL,
          longitude REAL NOT NULL,
          timestampUtc TEXT NOT NULL,
          attempts INTEGER NOT NULL DEFAULT 0
        )'''),
    );
    return _db!;
  }

  Future<int> enqueue(PendingEvent e) async {
    final db = await _database;
    return db.insert(_table, e.toMap()..remove('id'));
  }

  Future<List<PendingEvent>> pending() async {
    final db = await _database;
    final rows = await db.query(_table, orderBy: 'id ASC');
    return rows.map(PendingEvent.fromMap).toList();
  }

  Future<int> pendingCount() async {
    final db = await _database;
    final c = await db.rawQuery('SELECT COUNT(*) AS c FROM $_table');
    return (c.first['c'] as int?) ?? 0;
  }

  Future<void> remove(int id) async {
    final db = await _database;
    await db.delete(_table, where: 'id = ?', whereArgs: [id]);
  }

  Future<void> bumpAttempts(int id) async {
    final db = await _database;
    await db.rawUpdate('UPDATE $_table SET attempts = attempts + 1 WHERE id = ?', [id]);
  }

  Future<void> clearAll() async {
    final db = await _database;
    await db.delete(_table);
  }
}
