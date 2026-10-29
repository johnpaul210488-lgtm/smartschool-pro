import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { BufferJSON } from 'baileys';
import { createEncryptedAuth, hasSavedSession, deleteSavedSession } from '../src/encrypted-auth.js';

const options = (dir) => ({ dir, initCreds: () => ({ registered: true, secret: 'NEVER-PLAINTEXT' }),
  BufferJSON, appStateFromObject: value => ({ restored: value }) });

test('session and Signal keys are encrypted, reloadable, tamper-evident and resettable', async t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'qr-auth-test-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const auth = createEncryptedAuth(options(dir));
  assert.equal(hasSavedSession(dir), false);
  await auth.saveCreds();
  await auth.state.keys.set({ session: { '../../private@example': { phone: '+920001234567', key: Buffer.from('secret-key') } },
    'app-state-sync-key': { one: { token: 'PRIVATE-DATA' } } });
  assert.equal(hasSavedSession(dir), true);
  const files = fs.readdirSync(path.join(dir, 'auth'));
  assert.equal(files.length, 3);
  for (const file of files) {
    assert.match(file, /^(?:creds|[0-9a-f]{64})\.enc$/);
    const data = fs.readFileSync(path.join(dir, 'auth', file));
    assert.equal(data.includes(Buffer.from('PRIVATE-DATA')), false);
    assert.equal(data.includes(Buffer.from('NEVER-PLAINTEXT')), false);
    assert.equal(data.includes(Buffer.from('+920001234567')), false);
  }
  if (process.platform === 'win32') assert.notEqual(fs.readFileSync(path.join(dir, 'master.key')).length, 32);
  const reloaded = createEncryptedAuth(options(dir));
  assert.equal(reloaded.state.creds.secret, 'NEVER-PLAINTEXT');
  const value = await reloaded.state.keys.get('session', ['../../private@example']);
  assert.equal(value['../../private@example'].phone, '+920001234567');
  assert.deepEqual(value['../../private@example'].key, Buffer.from('secret-key'));
  const appState = await reloaded.state.keys.get('app-state-sync-key', ['one']);
  assert.equal(appState.one.restored.token, 'PRIVATE-DATA');
  await reloaded.state.keys.set({ session: { '../../private@example': null } });
  assert.equal((await reloaded.state.keys.get('session', ['../../private@example']))['../../private@example'], null);
  const credsFile = path.join(dir, 'auth', 'creds.enc');
  const packed = fs.readFileSync(credsFile); packed[packed.length - 1] ^= 1; fs.writeFileSync(credsFile, packed);
  assert.throws(() => createEncryptedAuth(options(dir)));
  deleteSavedSession(dir);
  assert.equal(hasSavedSession(dir), false);
});
