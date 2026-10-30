import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { EncryptedJsonStore } from '../src/encrypted-store.js';
import { useEncryptedAuthState } from '../src/auth-state.js';

async function tempFile(t) {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'smartschool-wa-'));
  t.after(() => fs.rm(directory, { recursive: true, force: true }));
  return path.join(directory, 'state.enc');
}

test('encrypted store round-trips Baileys buffers without plaintext leakage', async t => {
  const file = await tempFile(t);
  const key = crypto.randomBytes(32);
  const store = new EncryptedJsonStore(file, key);
  const value = { secret: 'plain-session-secret', bytes: Buffer.from([1, 2, 3]) };

  await store.save(value);
  const raw = await fs.readFile(file);
  assert.equal(raw.includes(Buffer.from(value.secret)), false);

  const restored = await store.load(() => null);
  assert.equal(restored.secret, value.secret);
  assert.deepEqual(Buffer.from(restored.bytes), value.bytes);
});

test('Baileys credentials and signal keys restore from one encrypted auth state', async t => {
  const file = await tempFile(t);
  const key = crypto.randomBytes(32);
  const first = await useEncryptedAuthState(file, key);
  first.state.creds.registered = true;
  first.state.creds.me = { id: '923001234567@s.whatsapp.net', name: 'School' };
  await first.state.keys.set({ 'pre-key': { '7': Buffer.from([7, 8, 9]) } });
  await first.saveCreds();

  const raw = await fs.readFile(file);
  assert.equal(raw.includes(Buffer.from('923001234567')), false);

  const restored = await useEncryptedAuthState(file, key);
  assert.equal(restored.isRegistered, true);
  assert.equal(restored.state.creds.me.id, '923001234567@s.whatsapp.net');
  const keys = await restored.state.keys.get('pre-key', ['7']);
  assert.deepEqual(Buffer.from(keys['7']), Buffer.from([7, 8, 9]));
});

test('encrypted store rejects tampering', async t => {
  const file = await tempFile(t);
  const store = new EncryptedJsonStore(file, crypto.randomBytes(32));
  await store.save({ ok: true });
  const raw = await fs.readFile(file);
  raw[raw.length - 1] ^= 0xff;
  await fs.writeFile(file, raw);

  await assert.rejects(() => store.load(() => null), /Unable to decrypt/);
});

test('queued saves preserve their own snapshots', async t => {
  const file = await tempFile(t);
  const store = new EncryptedJsonStore(file, crypto.randomBytes(32));
  const document = { value: 1 };
  const first = store.save(document);
  document.value = 2;
  const second = store.save(document);
  await Promise.all([first, second]);

  assert.equal((await store.load(() => null)).value, 2);
});
