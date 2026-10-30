import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

test('real packaged entry point starts inert, loopback-only and authenticated', async t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'qr-process-test-'));
  const secret = 'standalone-test-secret-'.repeat(3);
  const child = spawn(process.execPath, [path.join(root, 'src', 'index.js')], {
    cwd: root, stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, SMARTSCHOOL_QR_SECRET: secret, SMARTSCHOOL_QR_STATE_DIR: dir,
      SMARTSCHOOL_QR_PARENT_PID: String(process.pid) },
  });
  t.after(async () => {
    if (child.exitCode === null && child.signalCode === null) { const exited = once(child, 'exit'); child.kill(); await exited; }
    fs.rmSync(dir, { recursive: true, force: true });
  });
  const lines = readline.createInterface({ input: child.stdout });
  const [line] = await Promise.race([once(lines, 'line'), once(child, 'exit').then(() => { throw new Error('Child exited before ready'); }),
    new Promise((_, reject) => { const t = setTimeout(() => reject(new Error('Startup timeout')), 10000); t.unref(); })]);
  assert.match(line, /^READY \d+$/);
  const port = Number(line.substring(6));
  assert.equal((await fetch(`http://127.0.0.1:${port}/qr`)).status, 401);
  const response = await fetch(`http://127.0.0.1:${port}/status`, { headers: { Authorization: `Bearer ${secret}` } });
  assert.deepEqual(await response.json(), { ok: true, state: 'off', testOnly: true, accountFingerprint: null });
  assert.equal(fs.existsSync(path.join(dir, 'auth')), false);
});
