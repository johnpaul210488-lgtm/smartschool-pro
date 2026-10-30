import assert from 'node:assert/strict';
import test from 'node:test';
import { createGatewayServer } from '../src/http-server.js';

function listen(server) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => resolve(server.address().port));
  });
}

test('HTTP gateway exposes minimal health and protects all session data with bearer auth', async t => {
  const session = {
    snapshot: () => ({ status: 'disconnected' }),
    connect: async () => ({ status: 'connecting' }),
    logout: async () => ({ status: 'disconnected' })
  };
  const messages = {
    status: () => ({ provider: 'LinkedDevice', status: 'disconnected' }),
    send: async () => ({ providerMessageId: 'one', duplicate: false })
  };
  const logger = { warn() {}, error() {} };
  const config = { apiKey: 'a'.repeat(43), requestBodyLimitBytes: 32_768 };
  const server = createGatewayServer({ config, session, messages, logger });
  t.after(() => new Promise(resolve => server.close(resolve)));
  const port = await listen(server);
  const base = `http://127.0.0.1:${port}`;

  const health = await fetch(`${base}/health`);
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), {
    status: 'ok', sessionStatus: 'disconnected', version: '1.0.0'
  });

  const rejected = await fetch(`${base}/v1/session`);
  assert.equal(rejected.status, 401);
  assert.equal((await rejected.json()).error.code, 'unauthorized');

  const accepted = await fetch(`${base}/v1/session`, {
    headers: { Authorization: `Bearer ${config.apiKey}` }
  });
  assert.equal(accepted.status, 200);
  assert.equal((await accepted.json()).data.provider, 'LinkedDevice');
});
