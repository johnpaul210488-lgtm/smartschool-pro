import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import writeFileAtomic from 'write-file-atomic';
import { BufferJSON } from '@whiskeysockets/baileys';

const MAGIC = Buffer.from('SSWA01', 'ascii');
const NONCE_BYTES = 12;
const TAG_BYTES = 16;

function encrypt(plaintext, key) {
  const nonce = crypto.randomBytes(NONCE_BYTES);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, nonce);
  cipher.setAAD(MAGIC);
  const ciphertext = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return Buffer.concat([MAGIC, nonce, tag, ciphertext]);
}

function decrypt(payload, key) {
  const minimumLength = MAGIC.length + NONCE_BYTES + TAG_BYTES + 1;
  if (payload.length < minimumLength || !payload.subarray(0, MAGIC.length).equals(MAGIC)) {
    throw new Error('Encrypted store has an invalid header.');
  }

  let offset = MAGIC.length;
  const nonce = payload.subarray(offset, offset += NONCE_BYTES);
  const tag = payload.subarray(offset, offset += TAG_BYTES);
  const ciphertext = payload.subarray(offset);
  const decipher = crypto.createDecipheriv('aes-256-gcm', key, nonce);
  decipher.setAAD(MAGIC);
  decipher.setAuthTag(tag);
  return Buffer.concat([decipher.update(ciphertext), decipher.final()]).toString('utf8');
}

/**
 * Small AES-256-GCM JSON store. Writes are serialized and atomic; authentication
 * state, QR material and message bodies are never written in plaintext.
 */
export class EncryptedJsonStore {
  #filePath;
  #key;
  #writeTail = Promise.resolve();

  constructor(filePath, key) {
    if (!Buffer.isBuffer(key) || key.length !== 32) {
      throw new TypeError('EncryptedJsonStore requires a 32-byte key.');
    }
    this.#filePath = filePath;
    this.#key = Buffer.from(key);
  }

  async exists() {
    try {
      await fs.access(this.#filePath);
      return true;
    } catch (error) {
      if (error?.code === 'ENOENT') return false;
      throw error;
    }
  }

  async load(fallbackFactory) {
    try {
      const payload = await fs.readFile(this.#filePath);
      return JSON.parse(decrypt(payload, this.#key), BufferJSON.reviver);
    } catch (error) {
      if (error?.code === 'ENOENT') return fallbackFactory();
      throw new Error(`Unable to decrypt ${path.basename(this.#filePath)}: ${error.message}`, {
        cause: error
      });
    }
  }

  save(value) {
    // Serialize now so a later mutation cannot leak into an earlier queued write.
    const plaintext = JSON.stringify(value, BufferJSON.replacer);
    const payload = encrypt(plaintext, this.#key);
    const operation = this.#writeTail.then(async () => {
      await fs.mkdir(path.dirname(this.#filePath), { recursive: true, mode: 0o700 });
      await writeFileAtomic(this.#filePath, payload, { mode: 0o600 });
      await fs.chmod(this.#filePath, 0o600).catch(() => {});
    });
    this.#writeTail = operation.catch(() => {});
    return operation;
  }

  async clear() {
    await this.#writeTail;
    try {
      await fs.rm(this.#filePath, { force: true });
    } finally {
      this.#writeTail = Promise.resolve();
    }
  }
}

export const encryptedStoreInternals = Object.freeze({ encrypt, decrypt, MAGIC });
