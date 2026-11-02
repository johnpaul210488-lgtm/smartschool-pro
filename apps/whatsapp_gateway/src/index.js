import path from 'node:path';
import { createClient } from './client.js';
import { createDb } from './db.js';
import { createQueue } from './queue.js';
import { createApi } from './api.js';

const dir = process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR;
const secret = process.env.SMARTSCHOOL_WHATSAPP_SECRET;
const apiPort = Number(process.env.SMARTSCHOOL_WHATSAPP_API_PORT || 0);

if (!dir || !path.isAbsolute(dir)) {
  process.stderr.write('SMARTSCHOOL_WHATSAPP_STATE_DIR must be an absolute path.\n');
  process.exit(1);
}
if (!secret || secret.length < 16) {
  process.stderr.write('SMARTSCHOOL_WHATSAPP_SECRET must be at least 16 chars.\n');
  process.exit(1);
}

// --- Bootstrap layers ---
const db = createDb({ dir });

const client = createClient({
  dir,
  logger: console,
  onStatusChange: (st) => {
    console.log(`[whatsapp] status: ${st.state}`);
  },
  onQr: (qrDataUrl) => {
    console.log('[whatsapp] QR ready (data URL available via /api/session/qr)');
  },
  onMessage: (msg) => {
    console.log('[whatsapp] inbound message:', msg.from, msg.text ?? '(media)');
    // Store inbound message
    try {
      db.saveMessage({
        id: msg.id,
        wa_message_id: msg.id,
        contact_jid: msg.from,
        from_me: 0,
        message_type: msg.type,
        body: msg.text,
        raw_json: JSON.stringify(msg.raw),
        status: 'received',
        timestamp: msg.timestamp,
        created_at: new Date().toISOString(),
      });
    } catch (e) {
      console.error('[whatsapp] failed to save inbound:', e.message);
    }
  },
  onMessageStatus: (update) => {
    console.log('[whatsapp] message status:', update.id, update.status);
    // Best-effort: mark outbox items as sent if we can match
    queue.handleMessageStatus(update);
  },
});

// Connect auth from DB
const saved = db.loadSession();
if (saved) {
  client.setAuth(saved);
}

// Start queue processor
const queue = createQueue({
  db,
  sendText: (opts) => client.sendText(opts),
  sendMedia: (opts) => client.sendMedia(opts),
  getDailyCounter: (opts) => db.getDailyCounter(opts),
  incrementDailyCounter: (opts) => db.incrementDailyCounter(opts),
  logger: console,
  onMessageStatus: (update) => {
    console.log('[whatsapp-queue] status update:', update.id, update.status);
  },
});
queue.start(1000);

// Start HTTP API
const server = createApi({ client, queue, db, secret });
server.listen(apiPort, '127.0.0.1', () => {
  const port = server.address().port;
  process.stdout.write(`READY ${port}\n`);
  console.log(`[whatsapp] API listening on 127.0.0.1:${port}`);

  // Restore existing session if present
  if (saved && saved.state?.creds?.me?.id) {
    console.log('[whatsapp] Restoring existing session...');
    client.restore(saved);
  } else {
    console.log('[whatsapp] No session found. Call POST /api/session/connect to start pairing.');
  }
});

async function shutdown() {
  console.log('[whatsapp] Shutting down...');
  queue.stop();
  await client.stop();
  server.close();
  process.exit(0);
}

process.on('SIGTERM', () => void shutdown());
process.on('SIGINT', () => void shutdown());

// Kill self if parent process dies
const parentPid = Number(process.env.SMARTSCHOOL_WHATSAPP_PARENT_PID || 0);
if (parentPid > 0) {
  setInterval(() => {
    try { process.kill(parentPid, 0); } catch { void shutdown(); }
  }, 3000).unref();
}
