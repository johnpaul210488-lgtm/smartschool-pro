import crypto from 'node:crypto';
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

async function readOrCreateSecret(filePath, createValue) {
  try {
    return (await fs.readFile(filePath, 'utf8')).trim();
  } catch (error) {
    if (error?.code !== 'ENOENT') throw error;
  }

  const value = createValue();
  await writeFileAtomic(filePath, `${value}\n`, { encoding: 'utf8', mode: 0o600 });
  await fs.chmod(filePath, 0o600).catch(() => {});
  return value;
}

function decodeSessionKey(encoded) {
  const key = Buffer.from(encoded, 'base64url');
  if (key.length !== 32) {
    throw new Error('The gateway session key is invalid; expected 32 bytes encoded as base64url.');
  }
  return key;
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
    const document = JSON.stringify({ version: 1, apiKey }, null, 2);
    await writeFileAtomic(clientSecretPath, `${document}\n`, { encoding: 'utf8', mode: 0o600 });
    await fs.chmod(clientSecretPath, 0o600).catch(() => {});
  }
  if (typeof apiKey !== 'string' || apiKey.length < 32) {
    throw new Error(`The apiKey in ${clientSecretPath} is missing or invalid.`);
  }

  const encodedSessionKey = await readOrCreateSecret(
    path.join(stateDirectory, SESSION_KEY_FILE),
    () => crypto.randomBytes(32).toString('base64url')
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
    sessionKey: decodeSessionKey(encodedSessionKey),
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
