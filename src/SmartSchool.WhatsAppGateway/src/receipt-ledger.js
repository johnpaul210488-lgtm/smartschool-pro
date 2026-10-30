import crypto from 'node:crypto';
import { EncryptedJsonStore } from './encrypted-store.js';
import { GatewayError } from './errors.js';

const RETENTION_MS = 30 * 24 * 60 * 60 * 1_000;
const MAX_ENTRIES = 10_000;
const pakistanDateFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Karachi',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit'
});

function pakistanDay(date) {
  const parts = Object.fromEntries(
    pakistanDateFormatter.formatToParts(date).map(part => [part.type, part.value])
  );
  return `${parts.year}-${parts.month}-${parts.day}`;
}

function emptyLedger() {
  return { version: 1, entries: {} };
}

export class ReceiptLedger {
  #store;
  #key;
  #document;

  constructor(filePath, key) {
    this.#store = new EncryptedJsonStore(filePath, key);
    this.#key = Buffer.from(key);
  }

  async initialize() {
    this.#document = await this.#store.load(emptyLedger);
    if (!this.#document?.entries || typeof this.#document.entries !== 'object') {
      throw new Error('The encrypted send-receipt ledger is malformed.');
    }
    await this.#prune(new Date());
    return this;
  }

  recipientHash(normalizedE164) {
    return crypto.createHmac('sha256', this.#key).update(normalizedE164).digest('base64url');
  }

  receipt(idempotencyKey) {
    const entry = this.#document.entries[idempotencyKey];
    return entry ? { ...entry } : null;
  }

  async begin(idempotencyKey, recipientHash, now = new Date()) {
    const existing = this.#document.entries[idempotencyKey];
    if (existing) {
      if (existing.status === 'sent') {
        return { duplicate: true, providerMessageId: existing.providerMessageId };
      }
      throw new GatewayError(
        'delivery_unknown',
        'This request was already handed to WhatsApp and its final delivery state is unknown. ' +
          'It will not be sent again automatically to avoid a duplicate.',
        { status: 409, retryable: false }
      );
    }

    const day = pakistanDay(now);
    this.#document.entries[idempotencyKey] = {
      status: 'pending',
      recipientHash,
      day,
      startedAtUtc: now.toISOString(),
      updatedAtUtc: now.toISOString()
    };
    await this.#store.save(this.#document);
    return { duplicate: false };
  }

  async markSent(idempotencyKey, providerMessageId, now = new Date()) {
    const entry = this.#require(idempotencyKey);
    entry.status = 'sent';
    entry.providerMessageId = providerMessageId;
    entry.updatedAtUtc = now.toISOString();
    await this.#store.save(this.#document);
  }

  async markUnknown(idempotencyKey, failureCode, now = new Date()) {
    const entry = this.#require(idempotencyKey);
    entry.status = 'unknown';
    entry.failureCode = failureCode;
    entry.updatedAtUtc = now.toISOString();
    await this.#store.save(this.#document);
  }

  async cancelPending(idempotencyKey) {
    const entry = this.#document.entries[idempotencyKey];
    if (entry?.status === 'pending') {
      delete this.#document.entries[idempotencyKey];
      await this.#store.save(this.#document);
    }
  }

  usage(recipientHash, now = new Date()) {
    const day = pakistanDay(now);
    const hourAgo = now.getTime() - 60 * 60 * 1_000;
    let dayCount = 0;
    let recipientHourCount = 0;

    for (const entry of Object.values(this.#document.entries)) {
      if (!['pending', 'sent', 'unknown'].includes(entry.status)) continue;
      if (entry.day === day) dayCount += 1;
      if (entry.recipientHash === recipientHash) {
        const started = Date.parse(entry.startedAtUtc);
        if (Number.isFinite(started) && started >= hourAgo) recipientHourCount += 1;
      }
    }
    return { day, dayCount, recipientHourCount };
  }

  summary(now = new Date()) {
    const day = pakistanDay(now);
    const values = Object.values(this.#document.entries);
    return {
      day,
      attemptedToday: values.filter(entry => entry.day === day).length,
      sentToday: values.filter(entry => entry.day === day && entry.status === 'sent').length,
      unknownToday: values.filter(entry => entry.day === day && entry.status === 'unknown').length
    };
  }

  #require(idempotencyKey) {
    const entry = this.#document.entries[idempotencyKey];
    if (!entry) throw new Error(`Receipt ${idempotencyKey} does not exist.`);
    return entry;
  }

  async #prune(now) {
    const cutoff = now.getTime() - RETENTION_MS;
    const entries = Object.entries(this.#document.entries)
      .filter(([, entry]) => {
        const timestamp = Date.parse(entry.updatedAtUtc || entry.startedAtUtc);
        return !Number.isFinite(timestamp) || timestamp >= cutoff;
      })
      .sort((a, b) => Date.parse(b[1].updatedAtUtc) - Date.parse(a[1].updatedAtUtc))
      .slice(0, MAX_ENTRIES);

    if (entries.length !== Object.keys(this.#document.entries).length) {
      this.#document.entries = Object.fromEntries(entries);
      await this.#store.save(this.#document);
    }
  }
}

export const receiptLedgerInternals = Object.freeze({ pakistanDay });
