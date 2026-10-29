import test from 'node:test';
import assert from 'node:assert/strict';
import { createGatewayServer } from '../src/http.js';

const secret = 'test-secret-'.repeat(5);

test('loopback gateway requires a secret and explicit confirmations for every sensitive action', async t => {
  let starts = 0, unlinks = 0, sends = 0;
  const session = {
    status: () => ({ state: 'off', testOnly: true }), getQr: () => 'data:image/png;base64,TEST-QR',
    start: () => { starts++; return { ok: true }; },
    unlink: async () => { unlinks++; return { ok: true }; },
    sendTest: async () => { sends++; return { ok: true, result: 'submitted' }; },
  };
  const server = createGatewayServer({ session, secret });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  const base = `http://127.0.0.1:${server.address().port}`;
  const auth = { Authorization: `Bearer ${secret}` };
  const json = { ...auth, 'Content-Type': 'application/json' };
  assert.equal((await fetch(`${base}/status`)).status, 401);
  assert.equal((await fetch(`${base}/qr`, { headers: { Authorization: 'Bearer wrong' } })).status, 401);
  assert.equal((await fetch(`${base}/qr`, { headers: { ...auth, Origin: 'https://example.test' } })).status, 401);
  const q = await fetch(`${base}/qr`, { headers: auth });
  assert.equal(q.headers.get('cache-control'), 'no-store, private');
  assert.equal((await q.json()).qrDataUrl, 'data:image/png;base64,TEST-QR');
  assert.equal((await fetch(`${base}/session/start`, { method: 'POST', headers: json, body: '{}' })).status, 400);
  assert.equal(starts, 0);
  assert.equal((await fetch(`${base}/session/start`, { method: 'POST', headers: json, body: '{"riskConfirmed":true}' })).status, 200);
  assert.equal(starts, 1);
  assert.equal((await fetch(`${base}/session/unlink`, { method: 'POST', headers: json, body: '{}' })).status, 400);
  assert.equal(unlinks, 0);
  assert.equal((await fetch(`${base}/session/unlink`, { method: 'POST', headers: json, body: '{"confirm":true}' })).status, 200);
  assert.equal(unlinks, 1);
  assert.equal((await fetch(`${base}/test/send`, { method: 'POST', body: '{}' })).status, 401);
  assert.equal(sends, 0);
  assert.equal((await fetch(`${base}/test/send`, { method: 'POST', headers: json, body: '{}' })).status, 200);
  assert.equal(sends, 1);
  assert.throws(() => createGatewayServer({ session, secret: '' }));
});
