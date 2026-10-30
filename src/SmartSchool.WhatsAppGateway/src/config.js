import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import writeFileAtomic from 'write-file-atomic';

const CLIENT_SECRET_FILE = 'client-secret.json';
const SESSION_KEY_FILE = 'session.key';

function integerFromEnv(name, fallback, min, max) {
  const raw = process.env[name];
  if (raw === undefined || raw === '') return fallback;
  const value = Number.parseInt(raw, 10);
  if (!Number.isSafeInteger(value) || value < min || value > max) {
    throw new Error(`${name} must be an integer between ${min} and ${max}.`);
  }
  return value;
}

function booleanFromEnv(name, fallback = false) {
  const raw = process.env[name];
  if (raw === undefined || raw === '') return fallback;
  if (/^(1|true|yes)$/i.test(raw)) return true;
  if (/^(0|false|no)$/i.test(raw)) return false;
  throw new Error(`${name} must be true or false.`);
}

function defaultStateDirectory() {
  if (process.env.LOCALAPPDATA) {
    return path.join(process.env.LOCALAPPDATA, 'SmartSchool', 'WhatsAppGateway');
  }
  if (process.env.XDG_STATE_HOME) {
    return path.join(process.env.XDG_STATE_HOME, 'smartschool', 'whatsapp-gateway');
  }
  return path.join(os.homedir(), '.local', 'state', 'smartschool', 'whatsapp-gateway');
}

function windowsDpapi(value, operation) {
  const method = operation === 'protect' ? 'Protect' : 'Unprotect';
  const script = [
    'Add-Type -AssemblyName System.Security',
    '$v = [Convert]::FromBase64String([Console]::In.ReadLine())',
    `$r = [Security.Cryptography.ProtectedData]::${method}($v, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)`,
    '[Console]::Out.Write([Convert]::ToBase64String($r))'
  ].join('; ');
  const result = spawnSync(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', script],
    {
      input: `${value.toString('base64')}\n`,
      encoding: 'utf8',
      timeout: 15_000,
      windowsHide: true
    }
  );
  if (result.error || result.status !== 0 || !result.stdout?.trim()) {
    throw new Error('Cannot access the WhatsApp encryption key for this Windows user.');
  }
  return Buffer.from(result.stdout.trim(), 'base64');
}

async function writeExclusive(filePath, value) {
  const temporary = `${filePath}.${process.pid}.${crypto.randomBytes(8).toString('hex')}.tmp`;
  let handle;
  try {
    handle = await fs.open(temporary, 'wx', 0o600);
    await handle.writeFile(value);
    await handle.sync();
    await handle.close();
    handle = undefined;
    try {
      // Linking a fully-flushed temp file publishes it atomically without replacing
      // a secret another gateway process may already be using.
      await fs.link(temporary, filePath);
      return true;
    } catch (error) {
      if (error?.code === 'EEXIST') return false;
      throw error;
    }
  } finally {
    await handle?.close();
    await fs.rm(temporary, { force: true }).catch(() => {});
  }
}

function decodeRawSessionKey(encoded) {
  const key = Buffer.from(encoded.trim(), 'base64url');
  if (key.length !== 32) {
    throw new Error('The gateway session key is invalid; expected 32 bytes.');
  }
  return key;
}

async function loadOrCreateSessionKey(filePath) {
  let stored;
  try {
    stored = await fs.readFile(filePath);
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
    const key = crypto.randomBytes(32);
    stored = process.platform === 'win32'
      ? windowsDpapi(key, 'protect')
      : Buffer.from(`${key.toString('base64url')}\n`);
    if (await writeExclusive(filePath, stored)) {
      await fs.chmod(filePath, 0o600).catch(() => {});
      return key;
    }
    // Another gateway won the create race; use exactly its persisted key.
    stored = await fs.readFile(filePath);
  }

  if (process.platform !== 'win32') return decodeRawSessionKey(stored.toString('utf8'));
  try {
    const key = windowsDpapi(stored, 'unprotect');
    if (key.length !== 32) throw new Error('Invalid DPAPI key length.');
    return key;
  } catch (error) {
    // One-time migration for a pre-release plaintext base64url key.
    if (!/^[A-Za-z0-9_-]{43}\s*$/.test(stored.toString('utf8'))) throw error;
    const key = decodeRawSessionKey(stored.toString('utf8'));
    await writeFileAtomic(filePath, windowsDpapi(key, 'protect'), { mode: 0o600 });
    return key;
  }
}

export async function loadConfig() {
  const stateDirectory = path.resolve(
    process.env.SMARTSCHOOL_WHATSAPP_STATE_DIR || defaultStateDirectory()
  );
  await fs.mkdir(stateDirectory, { recursive: true, mode: 0o700 });
  await fs.chmod(stateDirectory, 0o700).catch(() => {});

  const clientSecretPath = path.join(stateDirectory, CLIENT_SECRET_FILE);
  let apiKey;
  try {
    const document = JSON.parse(await fs.readFile(clientSecretPath, 'utf8'));
    apiKey = document.apiKey;
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      throw new Error(`Cannot read ${clientSecretPath}: ${error.message}`);
    }
    apiKey = crypto.randomBytes(32).toString('base64url');
    const document = `${JSON.stringify({ version: 1, apiKey }, null, 2)}\n`;
    if (!await writeExclusive(clientSecretPath, document)) {
      // Another gateway won the create race; authenticate with its secret.
      apiKey = JSON.parse(await fs.readFile(clientSecretPath, 'utf8')).apiKey;
    }
    await fs.chmod(clientSecretPath, 0o600).catch(() => {});
  }
  if (typeof apiKey !== 'string' || apiKey.length < 32) {
    throw new Error(`The apiKey in ${clientSecretPath} is missing or invalid.`);
  }

  const sessionKey = await loadOrCreateSessionKey(
    path.join(stateDirectory, SESSION_KEY_FILE)
  );

  const host = process.env.SMARTSCHOOL_WHATSAPP_HOST || '127.0.0.1';
  const allowRemote = booleanFromEnv('SMARTSCHOOL_WHATSAPP_ALLOW_REMOTE', false);
  if (!allowRemote && host !== '127.0.0.1' && host !== '::1' && host !== 'localhost') {
    throw new Error(
      'Refusing to expose the linked-device gateway beyond loopback. ' +
      'Set SMARTSCHOOL_WHATSAPP_ALLOW_REMOTE=true only behind a secured private proxy.'
    );
  }

  const minSendIntervalMs = integerFromEnv(
    'SMARTSCHOOL_WHATSAPP_MIN_SEND_INTERVAL_MS', 6_000, 4_500, 300_000
  );
  const maxSendIntervalMs = integerFromEnv(
    'SMARTSCHOOL_WHATSAPP_MAX_SEND_INTERVAL_MS', 10_000, minSendIntervalMs, 600_000
  );

  return Object.freeze({
    host,
    port: integerFromEnv('SMARTSCHOOL_WHATSAPP_PORT', 5110, 1, 65_535),
    stateDirectory,
    clientSecretPath,
    apiKey,
    sessionKey,
    authStorePath: path.join(stateDirectory, 'auth-state.enc'),
    receiptStorePath: path.join(stateDirectory, 'send-receipts.enc'),
    minSendIntervalMs,
    maxSendIntervalMs,
    maxMessagesPerDay: integerFromEnv('SMARTSCHOOL_WHATSAPP_MAX_PER_DAY', 300, 1, 10_000),
    maxMessagesPerRecipientHour: integerFromEnv(
      'SMARTSCHOOL_WHATSAPP_MAX_PER_RECIPIENT_HOUR', 12, 1, 1_000
    ),
    maxPendingRequests: integerFromEnv('SMARTSCHOOL_WHATSAPP_MAX_PENDING', 100, 1, 10_000),
    qrTimeoutMs: integerFromEnv('SMARTSCHOOL_WHATSAPP_QR_TIMEOUT_MS', 60_000, 30_000, 300_000),
    requestBodyLimitBytes: integerFromEnv(
      'SMARTSCHOOL_WHATSAPP_BODY_LIMIT_BYTES', 32_768, 1_024, 1_048_576
    )
  });
}
