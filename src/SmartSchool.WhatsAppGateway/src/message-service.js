import { GatewayError } from './errors.js';
import { validateSendRequest } from './validation.js';

export class MessageService {
  #config;
  #session;
  #ledger;
  #gate;
  #logger;

  constructor({ config, session, ledger, gate, logger }) {
    this.#config = config;
    this.#session = session;
    this.#ledger = ledger;
    this.#gate = gate;
    this.#logger = logger;
  }

  async send(input) {
    const request = validateSendRequest(input);
    const existing = this.#ledger.receipt(request.idempotencyKey);
    if (existing?.status === 'sent') {
      return {
        providerMessageId: existing.providerMessageId,
        duplicate: true,
        acceptedAtUtc: existing.updatedAtUtc
      };
    }
    if (existing) {
      throw new GatewayError(
        'delivery_unknown',
        'This request was already handed to WhatsApp and will not be repeated automatically.',
        { status: 409, retryable: false }
      );
    }

    const recipientHash = this.#ledger.recipientHash(request.to);
    return this.#gate.run(async () => {
      // A duplicate may have completed while this request waited in the serial lane.
      const completedWhileWaiting = this.#ledger.receipt(request.idempotencyKey);
      if (completedWhileWaiting?.status === 'sent') {
        return {
          providerMessageId: completedWhileWaiting.providerMessageId,
          duplicate: true,
          acceptedAtUtc: completedWhileWaiting.updatedAtUtc
        };
      }
      if (completedWhileWaiting) {
        throw new GatewayError(
          'delivery_unknown',
          'This request was already handed to WhatsApp and will not be repeated automatically.',
          { status: 409, retryable: false }
        );
      }

      // Check immediately before persistence/send; queued callers cannot race the caps.
      const usage = this.#ledger.usage(recipientHash);
      if (usage.dayCount >= this.#config.maxMessagesPerDay) {
        throw new GatewayError(
          'daily_safety_limit',
          `The linked-device daily safety limit (${this.#config.maxMessagesPerDay}) has been reached.`,
          { status: 429, retryable: true, retryAfterSeconds: 3_600 }
        );
      }
      if (usage.recipientHourCount >= this.#config.maxMessagesPerRecipientHour) {
        throw new GatewayError(
          'recipient_safety_limit',
          'The per-recipient safety limit has been reached. Wait before sending another alert.',
          { status: 429, retryable: true, retryAfterSeconds: 900 }
        );
      }

      // A disconnected check before begin is safe and does not consume the idempotency key.
      if (!this.#session.snapshot().isConnected) {
        throw new GatewayError(
          'session_disconnected',
          'WhatsApp is not connected. The durable SmartSchool outbox will wait for recovery.',
          { status: 503, retryable: true, retryAfterSeconds: 15 }
        );
      }

      const startedAt = new Date();
      await this.#ledger.begin(request.idempotencyKey, recipientHash, startedAt);
      try {
        const providerMessageId = await this.#session.sendText(request.to, request.text);
        const completedAt = new Date();
        await this.#ledger.markSent(request.idempotencyKey, providerMessageId, completedAt);
        this.#logger.info(
          { messageId: providerMessageId, phoneSuffix: request.to.slice(-4) },
          'Transactional WhatsApp alert accepted.'
        );
        return {
          providerMessageId,
          duplicate: false,
          acceptedAtUtc: completedAt.toISOString()
        };
      } catch (error) {
        // sendText's pre-flight disconnected failure is known not to have submitted a message.
        if (error instanceof GatewayError && error.code === 'session_disconnected') {
          await this.#ledger.cancelPending(request.idempotencyKey);
          throw error;
        }

        await this.#ledger.markUnknown(
          request.idempotencyKey,
          error?.code || error?.name || 'send_failed'
        );
        this.#logger.error(
          { err: error, phoneSuffix: request.to.slice(-4) },
          'WhatsApp send outcome is unknown; automatic duplicate retry is blocked.'
        );
        throw new GatewayError(
          'delivery_unknown',
          'WhatsApp did not confirm the final send state. Automatic retry is blocked to avoid a duplicate; review the phone and outbox.',
          { status: 409, retryable: false }
        );
      }
    });
  }

  status() {
    return {
      ...this.#session.snapshot(),
      safety: {
        minSendIntervalMs: this.#config.minSendIntervalMs,
        maxSendIntervalMs: this.#config.maxSendIntervalMs,
        maxMessagesPerDay: this.#config.maxMessagesPerDay,
        maxMessagesPerRecipientHour: this.#config.maxMessagesPerRecipientHour,
        pendingRequests: this.#gate.pending,
        ...this.#ledger.summary()
      }
    };
  }
}
