import crypto from 'node:crypto';
import { GatewayError } from './errors.js';

const defaultSleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

/**
 * Strict single-file send lane with cryptographically random spacing. There is
 * deliberately no priority bypass: a burst cannot skip the safety interval.
 */
export class SafetyGate {
  #minIntervalMs;
  #maxIntervalMs;
  #maxPending;
  #sleep;
  #now;
  #randomInt;
  #tail = Promise.resolve();
  #pending = 0;
  #lastCompletedAt = 0;

  constructor({
    minIntervalMs,
    maxIntervalMs,
    maxPending,
    sleep = defaultSleep,
    now = () => Date.now(),
    randomInt = (min, max) => crypto.randomInt(min, max)
  }) {
    this.#minIntervalMs = minIntervalMs;
    this.#maxIntervalMs = maxIntervalMs;
    this.#maxPending = maxPending;
    this.#sleep = sleep;
    this.#now = now;
    this.#randomInt = randomInt;
  }

  get pending() {
    return this.#pending;
  }

  async run(operation) {
    if (this.#pending >= this.#maxPending) {
      throw new GatewayError(
        'gateway_queue_full',
        'The safe-send lane is full. The durable SmartSchool outbox will try again later.',
        { status: 429, retryable: true, retryAfterSeconds: 30 }
      );
    }

    this.#pending += 1;
    let release;
    const predecessor = this.#tail;
    this.#tail = new Promise(resolve => { release = resolve; });

    try {
      await predecessor;
      const interval = this.#randomInt(this.#minIntervalMs, this.#maxIntervalMs + 1);
      const elapsed = this.#lastCompletedAt === 0 ? interval : this.#now() - this.#lastCompletedAt;
      const wait = this.#lastCompletedAt === 0 ? 0 : Math.max(0, interval - elapsed);
      if (wait > 0) await this.#sleep(wait);
      return await operation();
    } finally {
      this.#lastCompletedAt = this.#now();
      this.#pending -= 1;
      release();
    }
  }
}
