import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

// The key is DPAPI-protected on Windows, bound to the Windows user running the
// school API. On other platforms filesystem permissions protect the raw key.
function dpapi(bytes, operation) {
  const call = operation === 'protect' ? 'Protect' : 'Unprotect';
  const script = [
    'Add-Type -AssemblyName System.Security',
    '$v = [Convert]::FromBase64String([Console]::In.ReadLine())',
    `$r = [Security.Cryptography.ProtectedData]::${call}($v, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)`,
    '[Console]::Out.Write([Convert]::ToBase64String($r))',
  ].join('; ');
  const p = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    input: bytes.toString('base64') + '\n', encoding: 'utf8', timeout: 15000, windowsHide: true,
  });
  if (p.error || p.status !== 0 || !p.stdout?.trim()) {
    throw new Error('Cannot access WhatsApp encryption key for this Windows user.');
  }
  return Buffer.from(p.stdout.trim(), 'base64');
}

function ensureDirectory(dir) {
  fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
  const stat = fs.lstatSync(dir);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Unsafe WhatsApp session directory.');
  if (process.platform !== 'win32') fs.chmodSync(dir, 0o700);
}

function loadKey(dir) {
  const file = path.join(dir, 'master.key');
  if (!fs.existsSync(file)) {
    const key = crypto.randomBytes(32);
    const stored = process.platform === 'win32' ? dpapi(key, 'protect') : key;
    // A concurrent process must never silently replace a key in use.
    try { fs.writeFileSync(file, stored, { flag: 'wx', mode: 0o600 }); }
    catch (error) { if (error.code !== 'EEXIST') throw error; }
  }
  const stat = fs.lstatSync(file);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new Error('Unsafe WhatsApp key file.');
  const stored = fs.readFileSync(file);
  const key = process.platform === 'win32' ? dpapi(stored, 'unprotect') : stored;
  if (key.length !== 32) throw new Error('WhatsApp encryption key is invalid.');
  return key;
}

export function hasSavedSession(dir) {
  return fs.existsSync(path.join(dir, 'auth', 'creds.enc'));
}

export function createEncryptedAuth({ dir, initCreds, BufferJSON, appStateFromObject }) {
  ensureDirectory(dir);
  const authDir = path.join(dir, 'auth');
  ensureDirectory(authDir);
  const key = loadKey(dir);
  const fileFor = (type, id) => type === 'creds'
    ? path.join(authDir, 'creds.enc')
    : path.join(authDir, crypto.createHash('sha256').update(`${type}\0${id}`).digest('hex') + '.enc');

  function read(file) {
    if (!fs.existsSync(file)) return null;
    const stat = fs.lstatSync(file);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new Error('Unsafe WhatsApp session file.');
    const packed = fs.readFileSync(file);
    if (packed.length < 29 || packed[0] !== 1) throw new Error('WhatsApp session file is invalid.');
    const decipher = crypto.createDecipheriv('aes-256-gcm', key, packed.subarray(1, 13));
    decipher.setAuthTag(packed.subarray(13, 29));
    const plain = Buffer.concat([decipher.update(packed.subarray(29)), decipher.final()]).toString('utf8');
    return JSON.parse(plain, BufferJSON.reviver);
  }

  function write(file, value) {
    const iv = crypto.randomBytes(12);
    const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
    const plain = JSON.stringify(value, BufferJSON.replacer);
    const ciphertext = Buffer.concat([cipher.update(plain, 'utf8'), cipher.final()]);
    const packed = Buffer.concat([Buffer.from([1]), iv, cipher.getAuthTag(), ciphertext]);
    const temp = `${file}.${crypto.randomBytes(8).toString('hex')}.tmp`;
    try {
      fs.writeFileSync(temp, packed, { flag: 'wx', mode: 0o600 });
      fs.renameSync(temp, file);
    } finally {
      try { fs.unlinkSync(temp); } catch (e) { if (e.code !== 'ENOENT') throw e; }
    }
  }

  const creds = read(fileFor('creds')) ?? initCreds();
  return {
    state: {
      creds,
      keys: {
        get: async (type, ids) => {
          const result = {};
          for (const id of ids) {
            let value = read(fileFor(type, id));
            if (type === 'app-state-sync-key' && value) value = appStateFromObject(value);
            result[id] = value;
          }
          return result;
        },
        set: async (updates) => {
          for (const [type, entries] of Object.entries(updates)) {
            for (const [id, value] of Object.entries(entries)) {
              const file = fileFor(type, id);
              if (value) write(file, value);
              else { try { fs.unlinkSync(file); } catch (e) { if (e.code !== 'ENOENT') throw e; } }
            }
          }
        },
      },
    },
    saveCreds: async () => write(fileFor('creds'), creds),
  };
}

export function deleteSavedSession(dir) {
  const authDir = path.join(dir, 'auth');
  if (!fs.existsSync(authDir)) return;
  const stat = fs.lstatSync(authDir);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Unsafe WhatsApp session directory.');
  fs.rmSync(authDir, { recursive: true });
}
