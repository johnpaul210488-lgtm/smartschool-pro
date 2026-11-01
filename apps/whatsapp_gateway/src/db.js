import Database from 'better-sqlite3';
import { join } from 'node:path';

/**
 * SQLite persistence layer for the WhatsApp service.
 * Stores: sessions, contacts, messages, outbox queue, daily counters, settings.
 */
export function createDb({ dir }) {
  const dbPath = join(dir, 'smartschool-whatsapp.db');
  const db = new Database(dbPath);
  db.pragma('journal_mode = WAL');
  db.pragma('foreign_keys = ON');

  db.exec(`
    CREATE TABLE IF NOT EXISTS sessions (
      id         TEXT PRIMARY KEY,
      creds      TEXT NOT NULL,
      keys       TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS contacts (
      jid              TEXT PRIMARY KEY,
      phone_number     TEXT,
      name             TEXT,
      notify           TEXT,
      profile_picture  TEXT,
      is_history_synced INTEGER DEFAULT 0,
      created_at       TEXT NOT NULL,
      updated_at       TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS messages (
      id             TEXT PRIMARY KEY,
      wa_message_id  TEXT UNIQUE,
      contact_jid    TEXT,
      from_me        INTEGER DEFAULT 0,
      message_type   TEXT,
      body           TEXT,
      raw_json       TEXT,
      local_media_path TEXT,
      status         TEXT DEFAULT 'pending',
      timestamp      TEXT NOT NULL,
      created_at     TEXT NOT NULL,
      FOREIGN KEY (contact_jid) REFERENCES contacts(jid)
    );

    CREATE TABLE IF NOT EXISTS outbox (
      id            TEXT PRIMARY KEY,
      phone         TEXT NOT NULL,
      type          TEXT NOT NULL,
      text          TEXT,
      media_path    TEXT,
      media_type    TEXT,
      file_name     TEXT,
      caption       TEXT,
      priority      INTEGER DEFAULT 10,
      status        TEXT DEFAULT 'pending',
      attempts      INTEGER DEFAULT 0,
      max_attempts  INTEGER DEFAULT 2,
      next_attempt_at TEXT NOT NULL,
      created_at    TEXT NOT NULL,
      updated_at    TEXT NOT NULL,
      error         TEXT
    );

    CREATE TABLE IF NOT EXISTS daily_counters (
      date                   TEXT PRIMARY KEY,
      marketing_count        INTEGER DEFAULT 0,
      transactional_count    INTEGER DEFAULT 0
    );

    CREATE TABLE IF NOT EXISTS settings (
      key   TEXT PRIMARY KEY,
      value TEXT
    );

    CREATE INDEX IF NOT EXISTS idx_outbox_status_next ON outbox(status, next_attempt_at);
    CREATE INDEX IF NOT EXISTS idx_messages_contact ON messages(contact_jid);
    CREATE INDEX IF NOT EXISTS idx_messages_status ON messages(status);
  `);

  const now = () => new Date().toISOString();

  // ---------- sessions ----------

  function saveSession({ id, creds, keys }) {
    db.prepare(
      'INSERT OR REPLACE INTO sessions(id, creds, keys, updated_at) VALUES (?, ?, ?, ?)'
    ).run(id, JSON.stringify(creds), JSON.stringify(keys), now());
  }

  function loadSession() {
    const row = db.prepare('SELECT creds, keys FROM sessions LIMIT 1').get();
    if (!row) return null;
    return { creds: JSON.parse(row.creds), keys: JSON.parse(row.keys) };
  }

  function deleteSession() {
    db.prepare('DELETE FROM sessions').run();
  }

  // ---------- contacts ----------

  function upsertContact(contact) {
    const ts = now();
    db.prepare(`
      INSERT OR REPLACE INTO contacts(jid, phone_number, name, notify, profile_picture, is_history_synced, created_at, updated_at)
      VALUES (@jid, @phone_number, @name, @notify, @profile_picture, @is_history_synced,
              COALESCE((SELECT created_at FROM contacts WHERE jid = @jid), @ts), @ts)
    `).run({ ...contact, created_at: ts, updated_at: ts });
  }

  function getContact(jid) {
    return db.prepare('SELECT * FROM contacts WHERE jid = ?').get(jid);
  }

  function listContacts({ search, limit = 50, offset = 0 } = {}) {
    let sql = 'SELECT * FROM contacts';
    const params = [];
    if (search) {
      sql += ' WHERE name LIKE ? OR phone_number LIKE ?';
      params.push(`%${search}%`, `%${search}%`);
    }
    sql += ' ORDER BY name ASC LIMIT ? OFFSET ?';
    params.push(limit, offset);
    return db.prepare(sql).all(...params);
  }

  // ---------- messages ----------

  function saveMessage(message) {
    db.prepare(`
      INSERT OR REPLACE INTO messages(id, wa_message_id, contact_jid, from_me, message_type, body, raw_json, local_media_path, status, timestamp, created_at)
      VALUES (@id, @wa_message_id, @contact_jid, @from_me, @message_type, @body, @raw_json, @local_media_path, @status, @timestamp, @created_at)
    `).run(message);
  }

  function updateMessageStatus(id, status) {
    db.prepare('UPDATE messages SET status = ? WHERE id = ?').run(status, id);
  }

  function listMessages({ contactJid, limit = 50, offset = 0 } = {}) {
    let sql = 'SELECT * FROM messages';
    const params = [];
    if (contactJid) {
      sql += ' WHERE contact_jid = ?';
      params.push(contactJid);
    }
    sql += ' ORDER BY timestamp DESC LIMIT ? OFFSET ?';
    params.push(limit, offset);
    return db.prepare(sql).all(...params);
  }

  // ---------- outbox ----------

  function enqueueOutbox(item) {
    const ts = now();
    db.prepare(`
      INSERT INTO outbox(id, phone, type, text, media_path, media_type, file_name, caption, priority, status, attempts, max_attempts, next_attempt_at, created_at, updated_at, error)
      VALUES (@id, @phone, @type, @text, @media_path, @media_type, @file_name, @caption, @priority, 'pending', 0, @max_attempts, @next_attempt_at, @ts, @ts, NULL)
    `).run(item);
  }

  function pendingOutbox({ limit = 100 } = {}) {
    const ts = now();
    return db.prepare(`
      SELECT * FROM outbox
      WHERE status = 'pending' AND next_attempt_at <= ?
      ORDER BY priority ASC, next_attempt_at ASC
      LIMIT ?
    `).all(ts, limit);
  }

  function markOutboxSending(id) {
    db.prepare('UPDATE outbox SET status = ?, updated_at = ? WHERE id = ?').run('sending', now(), id);
  }

  function markOutboxSent(id) {
    db.prepare('UPDATE outbox SET status = ?, updated_at = ? WHERE id = ?').run('sent', now(), id);
  }

  function markOutboxFailed(id, error, nextAttempt) {
    const ts = now();
    if (nextAttempt) {
      db.prepare('UPDATE outbox SET status = ?, attempts = attempts + 1, next_attempt_at = ?, updated_at = ?, error = ? WHERE id = ?')
        .run('pending', nextAttempt, ts, error, id);
    } else {
      db.prepare('UPDATE outbox SET status = ?, attempts = attempts + 1, updated_at = ?, error = ? WHERE id = ?')
        .run('failed', ts, error, id);
    }
  }

  function outboxCount({ status } = {}) {
    if (status) {
      return db.prepare('SELECT COUNT(*) AS c FROM outbox WHERE status = ?').get(status).c;
    }
    return db.prepare('SELECT COUNT(*) AS c FROM outbox').get().c;
  }

  // ---------- daily counters ----------

  function incrementDailyCounter({ date, type }) {
    const col = type === 'marketing' ? 'marketing_count' : 'transactional_count';
    db.prepare(`
      INSERT INTO daily_counters(date, marketing_count, transactional_count) VALUES (?, 0, 0)
      ON CONFLICT(date) DO NOTHING
    `).run(date);
    db.prepare(`UPDATE daily_counters SET ${col} = ${col} + 1 WHERE date = ?`).run(date);
  }

  function getDailyCounter({ date }) {
    return (
      db.prepare('SELECT * FROM daily_counters WHERE date = ?').get(date) ||
      { date, marketing_count: 0, transactional_count: 0 }
    );
  }

  // ---------- settings ----------

  function setSetting(key, value) {
    db.prepare('INSERT OR REPLACE INTO settings(key, value) VALUES (?, ?)').run(key, value);
  }

  function getSetting(key, fallback = null) {
    const row = db.prepare('SELECT value FROM settings WHERE key = ?').get(key);
    return row ? row.value : fallback;
  }

  // ---------- stats ----------

  function stats() {
    return {
      outboxPending: outboxCount({ status: 'pending' }),
      outboxSent: outboxCount({ status: 'sent' }),
      outboxFailed: outboxCount({ status: 'failed' }),
      totalMessages: db.prepare('SELECT COUNT(*) AS c FROM messages').get().c,
    };
  }

  return {
    saveSession,
    loadSession,
    deleteSession,
    upsertContact,
    getContact,
    listContacts,
    saveMessage,
    updateMessageStatus,
    listMessages,
    enqueueOutbox,
    pendingOutbox,
    markOutboxSending,
    markOutboxSent,
    markOutboxFailed,
    outboxCount,
    incrementDailyCounter,
    getDailyCounter,
    setSetting,
    getSetting,
    stats,
  };
}
