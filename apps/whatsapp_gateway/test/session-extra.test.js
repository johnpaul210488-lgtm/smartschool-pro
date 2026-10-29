import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { createSession } from '../src/session.js';

const delay = (n) => new Promise(resolve => setTimeout(resolve, n));
function fixture({ timeout = 300000, save = async () => {} } = {}) {
  const sockets = [];
  const auth = { state: { creds: { registered: false }, keys: {} }, saveCreds: save };
  const session = createSession({ dir: '/test', createAuth: () => auth, hasSession: () => false,
    deleteSession: () => {}, logger: {}, pairingTimeoutMs: timeout, reconnectDelay: () => 1,
    disconnectReason: { loggedOut: 401, forbidden: 403, connectionReplaced: 440, badSession: 500, multideviceMismatch: 411, restartRequired: 515 },
    proto: { HistorySync: { HistorySyncType: { INITIAL_BOOTSTRAP: 0 } } }, renderQr: async () => 'data:image/png;base64,test',
    makeSocket: () => {
      const sock = { ev: new EventEmitter(), end: async () => {}, logout: async () => {} };
      sockets.push(sock); return sock;
    } });
  return { session, sockets, auth };
}

test('after QR pairing, saved me/account permit automatic reconnect even with registered=false', async () => {
  const f = fixture();
  f.session.start();
  Object.assign(f.auth.state.creds, { me: { id: '923001234567:1@s.whatsapp.net' }, account: {} });
  f.sockets[0].ev.emit('creds.update', {});
  f.sockets[0].ev.emit('connection.update', { connection: 'close', lastDisconnect: { error: { output: { statusCode: 515 } } } });
  await delay(550); // The required post-pair restart uses a fixed 500ms delay.
  assert.equal(f.sockets.length, 2);
  assert.equal(f.auth.state.creds.registered, false);
  await f.session.stop();
});

test('unattended QR expires and encryption failure blocks the session', async () => {
  const f = fixture({ timeout: 20 });
  f.session.start();
  f.sockets[0].ev.emit('connection.update', { qr: 'secret' });
  await delay(45);
  assert.equal(f.session.status().state, 'needs_link');
  assert.equal(f.session.getQr(), null);

  const broken = fixture({ save: async () => { throw new Error('private session details'); } });
  broken.session.start();
  broken.sockets[0].ev.emit('creds.update', {});
  await delay(10);
  assert.equal(broken.session.status().state, 'session_error');
  assert.equal(broken.session.start().ok, false);
  await broken.session.stop();
});
