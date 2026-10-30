import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { loadConfig } from '../src/config.js';

test('gateway creates stable local secrets and DPAPI-wraps the Windows session key', async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'smartschool-wa-config-'));
  const previous = process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR;
  process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR = dir;
  t.after(async () => {
    if (previous === undefined) delete process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR;
    else process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR = previous;
    await fs.rm(dir, { recursive: true, force: true });
  });

  const [first, second] = await Promise.all([loadConfig(), loadConfig()]);
  assert.equal(first.apiKey, second.apiKey);
  assert.deepEqual(first.sessionKey, second.sessionKey);
  assert.equal(first.sessionKey.length, 32);

  if (process.platform === 'win32') {
    const stored = await fs.readFile(path.join(dir, 'session.key'));
    assert.equal(stored.includes(first.sessionKey), false);
    assert.equal(stored.includes(Buffer.from(first.sessionKey.toString('base64url'))), false);
  }
});
