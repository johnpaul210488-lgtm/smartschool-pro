import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { randomUUID } from 'node:crypto';
import { createSession } from '../src/session.js';

function fixture({ registered = false, saved = false, send, save, pairingTimeoutMs = 300000 } = {}) {
  const sockets = [], sent = [];
  let deletes = 0;
  // Baileys QR pairing persists me/account but can leave registered=false.
  const creds = registered ? { registered: false, me: { id: '923001234567:4@s.whatsapp.net' }, account: {} } : { registered: false };
  const auth = { state: { creds, keys: {} }, saveCreds: save ?? (async () => {}) };
  const disconnectReason = { loggedOut: 401, forbidden: 403, connectionReplaced: 440, badSession: 500, multideviceMismatch: 411, restartRequired: 515 };
  const session = createSession({ dir: '/test', createAuth: () => auth, hasSession: () => saved,
    deleteSession: () => { deletes++; }, disconnectReason, logger: {},
    proto: { HistorySync: { HistorySyncType: { INITIAL_BOOTSTRAP: 0 } } },
    renderQr: async qr => `data:image/png;base64,${qr}`,
    reconnectDelay: () => 1, sendTimeoutMs: 50, pairingTimeoutMs,
    makeSocket: config => {
      const s = { ev: new EventEmitter(), config,
        sendMessage: async (...args) => { sent.push(args); return send ? send(...args) : {}; },
        end: async () => {}, logout: async () => {} };
      sockets.push(s); return s;
    },
  });
  return { session, sockets, sent, auth, get deletes() { return deletes; } };
}
const tick = () => new Promise(resolve => setTimeout(resolve, 10));

test('pairing is explicit; sends only consented synthetic tests once per request', async () => {
  const f = fixture();
  f.session.restore();
  assert.equal(f.sockets.length, 0);
  assert.equal(f.session.status().state, 'off');
  assert.equal(f.session.start().ok, true);
  const sock = f.sockets[0];
  assert.equal(sock.config.syncFullHistory, false);
  assert.equal(sock.config.shouldSyncHistoryMessage({ syncType: 0 }), true);
  assert.equal(sock.config.shouldSyncHistoryMessage({ syncType: 1 }), false);
  sock.ev.emit('connection.update', { qr: 'PRIVATE-QR' });
  await tick();
  assert.equal(f.session.status().state, 'awaiting_scan');
  assert.equal(f.session.getQr(), 'data:image/png;base64,PRIVATE-QR');
  sock.ev.emit('connection.update', { connection: 'open' });
  assert.equal(f.session.getQr(), null);
  assert.equal(f.session.status().state, 'connected');
  const input = { phone: '+923001234567', type: 'pick', consentConfirmed: true, requestId: randomUUID() };
  assert.deepEqual(await f.session.sendTest({ ...input, consentConfirmed: false }),
    { ok: false, result: 'not_sent', error: 'Provide a valid test number, sample and consent.' });
  assert.equal((await f.session.sendTest({ ...input, phone: '03001234567' })).result, 'not_sent');
  assert.equal((await f.session.sendTest({ ...input, type: 'arbitrary', text: 'real student' })).result, 'not_sent');
  assert.equal((await f.session.sendTest(input)).result, 'submitted');
  assert.equal((await f.session.sendTest(input)).result, 'submitted');
  assert.equal(f.sent.length, 1);
  assert.equal(f.sent[0][0], '923001234567@s.whatsapp.net');
  assert.match(f.sent[0][1].text, /TEST ONLY/);
  assert.doesNotMatch(f.sent[0][1].text, /real student/i);
  const drop = await f.session.sendTest({ ...input, type: 'drop', requestId: randomUUID() });
  assert.equal(drop.result, 'submitted');
  assert.match(f.sent[1][1].text, /DROPPED OFF/);
  await f.session.stop();
  assert.equal(f.deletes, 0);
});

test('a send error is ambiguous and the same request is not automatically repeated', async () => {
  const f = fixture({ send: () => { throw new Error('network error with sensitive content'); } });
  f.session.start();
  f.sockets[0].ev.emit('connection.update', { connection: 'open' });
  const request = { phone: '+923001234567', type: 'pick', consentConfirmed: true, requestId: randomUUID() };
  const result = await f.session.sendTest(request);
  assert.equal(result.result, 'unknown');
  assert.doesNotMatch(result.error, /sensitive/);
  assert.deepEqual(await f.session.sendTest(request), result);
  assert.equal(f.sent.length, 1);
  await f.session.stop();
});

test('a timeout is unknown, and repeated request IDs do not trigger another attempt', async () => {
  const f = fixture({ send: () => new Promise(() => {}) });
  f.session.start();
  f.sockets[0].ev.emit('connection.update', { connection: 'open' });
  const request = { phone: '+923001234567', type: 'drop', consentConfirmed: true, requestId: randomUUID() };
  // Keep the test event loop alive while the deliberately unreferenced timeout fires.
  const result = await Promise.all([f.session.sendTest(request), f.session.sendTest(request), new Promise(r => setTimeout(r, 70))]);
  assert.equal(result[0].result, 'unknown');
  assert.equal(result[1].result, 'unknown');
  assert.equal(f.sent.length, 1);
  await f.session.stop();
});

test('only a registered session auto-restores, reconnects transiently and requires explicit unlink after logout', async () => {
  const partial = fixture({ saved: true, registered: false });
  partial.auth.state.creds.registered = true; // Pair-code flag alone is not a linked QR session.
  partial.session.restore();
  assert.equal(partial.session.status().state, 'needs_link');
  assert.equal(partial.sockets.length, 0);

  const f = fixture({ saved: true, registered: true });
  f.session.restore();
  assert.equal(f.sockets.length, 1);
  f.sockets[0].ev.emit('connection.update', { qr: 'must-not-display' });
  await tick();
  assert.equal(f.session.getQr(), null);
  f.sockets[0].ev.emit('connection.update', { connection: 'close', lastDisconnect: { error: { output: { statusCode: 408 } } } });
  assert.equal(f.session.status().state, 'reconnecting');
  await tick();
  assert.equal(f.sockets.length, 2);
  f.sockets[1].ev.emit('connection.update', { connection: 'close', lastDisconnect: { error: { output: { statusCode: 401 } } } });
  assert.equal(f.session.status().state, 'needs_relink');
  assert.equal(f.session.start().ok, false);
  const result = await f.session.unlink();
  assert.equal(result.ok, true);
  assert.equal(result.phoneUnlinked, false);
  assert.equal(f.deletes, 1);
  assert.equal(f.session.status().state, 'off');
});
