import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { ReceiptLedger, receiptLedgerInternals } from '../src/receipt-ledger.js';
import { SafetyGate } from '../src/safety-gate.js';
import { MessageService } from '../src/message-service.js';

async function fixture(t) {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'smartschool-ledger-'));
  t.after(() => fs.rm(directory, { recursive: true, force: true }));
  const ledger = await new ReceiptLedger(
    path.join(directory, 'receipts.enc'),
    crypto.randomBytes(32)
  ).initialize();
  return ledger;
}

test('receipt ledger returns a completed idempotent send without duplicating it', async t => {
  const ledger = await fixture(t);
  const recipient = ledger.recipientHash('+923001234567');
  const key = '12345678-1234-1234-1234-123456789abc';
  const now = new Date('2026-10-30T06:00:00.000Z');

  assert.deepEqual(await ledger.begin(key, recipient, now), { duplicate: false });
  await ledger.markSent(key, 'WA-MESSAGE-1', now);
  assert.deepEqual(await ledger.begin(key, recipient, now), {
    duplicate: true,
    providerMessageId: 'WA-MESSAGE-1'
  });
  assert.equal(ledger.usage(recipient, now).dayCount, 1);
});

test('pending/unknown receipts fail closed instead of risking duplicate sends', async t => {
  const ledger = await fixture(t);
  const key = 'pending-request-1234567890';
  const recipient = ledger.recipientHash('+923001234567');
  await ledger.begin(key, recipient);

  await assert.rejects(() => ledger.begin(key, recipient), error => {
    assert.equal(error.code, 'delivery_unknown');
    assert.equal(error.retryable, false);
    return true;
  });
});

test('concurrent duplicate requests result in one provider send', async t => {
  const ledger = await fixture(t);
  let sends = 0;
  const session = {
    snapshot: () => ({ isConnected: true }),
    sendText: async () => { sends += 1; return 'WA-ONE'; }
  };
  const gate = new SafetyGate({
    minIntervalMs: 4_500,
    maxIntervalMs: 4_500,
    maxPending: 10,
    now: () => 1_000,
    randomInt: () => 4_500,
    sleep: async () => {}
  });
  const service = new MessageService({
    config: { maxMessagesPerDay: 300, maxMessagesPerRecipientHour: 12 },
    session,
    ledger,
    gate,
    logger: { info() {}, error() {} }
  });
  const request = {
    to: '+923001234567',
    text: 'Bus alert',
    idempotencyKey: 'same-request-1234567890',
    classification: 'transactional'
  };

  const results = await Promise.all([service.send(request), service.send(request)]);
  assert.equal(sends, 1);
  assert.deepEqual(results.map(result => result.duplicate).sort(), [false, true]);
});

test('Pakistan day boundary uses Asia/Karachi instead of server local time', () => {
  assert.equal(receiptLedgerInternals.pakistanDay(new Date('2026-10-29T18:59:59Z')), '2026-10-29');
  assert.equal(receiptLedgerInternals.pakistanDay(new Date('2026-10-29T19:00:00Z')), '2026-10-30');
});
