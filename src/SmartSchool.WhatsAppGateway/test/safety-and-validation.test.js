import assert from 'node:assert/strict';
import test from 'node:test';
import { SafetyGate } from '../src/safety-gate.js';
import { normalizePhone, validateSendRequest } from '../src/validation.js';

test('phone normalization accepts Pakistani local and E.164 formats', () => {
  assert.equal(normalizePhone('0300 1234567'), '+923001234567');
  assert.equal(normalizePhone('3001234567'), '+923001234567');
  assert.equal(normalizePhone('whatsapp:+923001234567'), '+923001234567');
  assert.throws(() => normalizePhone('1203630@g.us'), /individual phone/);
});

test('send validation rejects bulk/marketing classifications and weak idempotency keys', () => {
  assert.throws(() => validateSendRequest({
    to: '+923001234567', text: 'Hello', idempotencyKey: 'short', classification: 'transactional'
  }), /idempotencyKey/);
  assert.throws(() => validateSendRequest({
    to: '+923001234567', text: 'Hello', idempotencyKey: '1234567890abcdef', classification: 'marketing'
  }), /transactional school alerts only/);
});

test('safety gate serializes sends and enforces randomized minimum spacing', async () => {
  let now = 1_000;
  const waits = [];
  const order = [];
  const gate = new SafetyGate({
    minIntervalMs: 6_000,
    maxIntervalMs: 10_000,
    maxPending: 5,
    now: () => now,
    randomInt: () => 7_000,
    sleep: async milliseconds => { waits.push(milliseconds); now += milliseconds; }
  });

  const first = gate.run(async () => { order.push('first'); });
  const second = gate.run(async () => { order.push('second'); });
  await Promise.all([first, second]);

  assert.deepEqual(order, ['first', 'second']);
  assert.deepEqual(waits, [7_000]);
  assert.equal(gate.pending, 0);
});

test('safety gate fails closed when its bounded lane is full', async () => {
  let release;
  const held = new Promise(resolve => { release = resolve; });
  const gate = new SafetyGate({
    minIntervalMs: 4_500,
    maxIntervalMs: 4_500,
    maxPending: 1
  });
  const first = gate.run(() => held);
  await assert.rejects(() => gate.run(async () => {}), error => {
    assert.equal(error.code, 'gateway_queue_full');
    assert.equal(error.retryable, true);
    return true;
  });
  release();
  await first;
});
