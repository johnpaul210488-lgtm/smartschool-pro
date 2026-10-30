import crypto from 'node:crypto';
import { Boom } from '@hapi/boom';
import QRCode from 'qrcode';
import {
  Browsers,
  DisconnectReason,
  fetchLatestBaileysVersion,
  makeWASocket
} from '@whiskeysockets/baileys';
import { useEncryptedAuthState } from './auth-state.js';
import { GatewayError } from './errors.js';

const TERMINAL_REASONS = new Set([
  DisconnectReason.loggedOut,
  DisconnectReason.badSession,
  DisconnectReason.multideviceMismatch
]);
const RECONNECT_REASONS = new Set([
  DisconnectReason.connectionClosed,
  DisconnectReason.connectionLost,
  DisconnectReason.timedOut,
  DisconnectReason.restartRequired,
  DisconnectReason.unavailableService
]);

function wait(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function withTimeout(promise, milliseconds, label) {
  return Promise.race([
    promise,
    wait(milliseconds).then(() => { throw new Error(`${label} timed out.`); })
  ]);
}

function statusCodeOf(error) {
  if (!error) return DisconnectReason.connectionClosed;
  if (Number.isInteger(error?.output?.statusCode)) return error.output.statusCode;
  try {
    return new Boom(error).output.statusCode;
  } catch {
    return DisconnectReason.connectionClosed;
  }
}

function displayPhone(user) {
  const digits = user?.id?.split('@')[0]?.split(':')[0]?.replace(/\D/g, '');
  return digits ? `+${digits}` : null;
}

export class WhatsAppSessionManager {
  #config;
  #logger;
  #baileysLogger;
  #auth;
  #socket = null;
  #generation = 0;
  #connectPromise = null;
  #reconnectTimer = null;
  #reconnectAttempt = 0;
  #pairingRequested = false;
  #outgoingMessages = new Map();
  #status = 'disconnected';
  #connectedPhone = null;
  #qrDataUrl = null;
  #qrExpiresAtUtc = null;
  #lastError = null;
  #stopping = false;

  constructor(config, logger) {
    this.#config = config;
    this.#logger = logger;
    this.#baileysLogger = logger.child({ component: 'baileys' });
  }

  async initialize() {
    this.#auth = await useEncryptedAuthState(this.#config.authStorePath, this.#config.sessionKey);
    if (this.#auth.isRegistered) {
      this.#logger.info('Encrypted linked-device credentials found; restoring the WhatsApp session.');
      this.#pairingRequested = true;
      await this.connect({ riskAccepted: true, automatic: true });
    }
    return this;
  }

  snapshot() {
    return {
      provider: 'LinkedDevice',
      status: this.#status,
      isConnected: this.#status === 'connected',
      hasSavedSession: Boolean(this.#auth?.isRegistered),
      connectedPhone: this.#connectedPhone,
      qrDataUrl: this.#qrDataUrl,
      qrExpiresAtUtc: this.#qrExpiresAtUtc,
      lastError: this.#lastError,
      reconnectAttempt: this.#reconnectAttempt
    };
  }

  async connect({ riskAccepted, automatic = false } = {}) {
    if (this.#stopping) {
      throw new GatewayError('gateway_stopping', 'The gateway is shutting down.', {
        status: 503,
        retryable: true,
        retryAfterSeconds: 10
      });
    }
    if (this.#status === 'connected' || this.#status === 'connecting' || this.#status === 'qr') {
      return this.snapshot();
    }
    if (this.#status === 'blocked') {
      throw new GatewayError(
        'account_forbidden',
        'WhatsApp refused this account. Automatic reconnect is disabled; stop sending and use WhatsApp review/support.',
        { status: 409, retryable: false }
      );
    }
    if (!this.#auth.isRegistered && !riskAccepted) {
      throw new GatewayError(
        'risk_acceptance_required',
        'Linked-device mode uses an unofficial WhatsApp Web client. An administrator must accept ' +
          'the WhatsApp terms/compliance notice before a QR code is created.',
        { status: 428, retryable: false }
      );
    }

    if (!automatic) this.#pairingRequested = true;
    if (this.#connectPromise) return this.#connectPromise;
    this.#connectPromise = this.#openSocket().finally(() => { this.#connectPromise = null; });
    await this.#connectPromise;
    return this.snapshot();
  }

  async #openSocket() {
    this.#clearReconnectTimer();
    const generation = ++this.#generation;
    this.#status = 'connecting';
    this.#qrDataUrl = null;
    this.#qrExpiresAtUtc = null;
    this.#lastError = null;

    let version;
    try {
      const latest = await fetchLatestBaileysVersion({ timeout: 8_000 });
      version = latest.version;
      if (!latest.isLatest) this.#logger.warn('Could not fetch the latest Baileys protocol version; using bundled version.');
    } catch (error) {
      this.#logger.warn({ err: error }, 'Protocol version lookup failed; using Baileys bundled version.');
    }

    const socket = makeWASocket({
      ...(version ? { version } : {}),
      auth: this.#auth.state,
      logger: this.#baileysLogger,
      browser: Browsers.windows('SmartSchool Pro'),
      printQRInTerminal: false,
      mobile: false,
      qrTimeout: this.#config.qrTimeoutMs,
      markOnlineOnConnect: false,
      syncFullHistory: false,
      shouldSyncHistoryMessage: () => false,
      generateHighQualityLinkPreview: false,
      emitOwnEvents: false,
      maxMsgRetryCount: 2,
      shouldIgnoreJid: jid => jid?.endsWith('@g.us') || jid === 'status@broadcast',
      getMessage: async key => key?.id ? this.#outgoingMessages.get(key.id) : undefined
    });
    this.#socket = socket;

    socket.ev.on('creds.update', () => {
      this.#auth.saveCreds().catch(error => {
        this.#logger.error({ err: error }, 'Failed to persist encrypted WhatsApp credentials.');
      });
    });

    socket.ev.on('connection.update', update => {
      this.#handleConnectionUpdate(generation, update).catch(error => {
        this.#logger.error({ err: error }, 'Connection update handler failed.');
      });
    });
  }

  async #handleConnectionUpdate(generation, update) {
    if (generation !== this.#generation || this.#stopping) return;

    if (update.qr) {
      const qrForThisRender = update.qr;
      const dataUrl = await QRCode.toDataURL(qrForThisRender, {
        errorCorrectionLevel: 'M',
        margin: 2,
        width: 360
      });
      if (generation !== this.#generation) return;
      this.#qrDataUrl = dataUrl;
      this.#qrExpiresAtUtc = new Date(Date.now() + this.#config.qrTimeoutMs).toISOString();
      this.#status = 'qr';
      this.#lastError = null;
      this.#logger.info('A new WhatsApp linking QR code is ready.');
    }

    if (update.connection === 'connecting' && this.#status !== 'qr') {
      this.#status = 'connecting';
    }

    if (update.connection === 'open') {
      this.#status = 'connected';
      this.#connectedPhone = displayPhone(this.#socket?.user || this.#auth.state.creds.me);
      this.#qrDataUrl = null;
      this.#qrExpiresAtUtc = null;
      this.#lastError = null;
      this.#reconnectAttempt = 0;
      this.#logger.info({ phoneSuffix: this.#connectedPhone?.slice(-4) }, 'WhatsApp linked-device session connected.');
      return;
    }

    if (update.connection !== 'close') return;
    const reason = statusCodeOf(update.lastDisconnect?.error);
    this.#socket = null;
    this.#qrDataUrl = null;
    this.#qrExpiresAtUtc = null;

    if (TERMINAL_REASONS.has(reason)) {
      this.#status = 'logged_out';
      this.#connectedPhone = null;
      this.#lastError = 'WhatsApp logged this linked device out. Link it again with a fresh QR code.';
      this.#pairingRequested = false;
      await this.#auth.clear();
      this.#auth = await useEncryptedAuthState(this.#config.authStorePath, this.#config.sessionKey);
      this.#logger.warn({ reason }, 'WhatsApp session credentials were invalidated and cleared.');
      return;
    }

    if (reason === DisconnectReason.connectionReplaced) {
      this.#status = 'logged_out';
      this.#lastError = 'This linked session was replaced by another WhatsApp connection.';
      this.#pairingRequested = false;
      this.#logger.warn('WhatsApp linked session was replaced; automatic reconnect is disabled.');
      return;
    }

    if (reason === DisconnectReason.forbidden) {
      this.#status = 'blocked';
      this.#lastError = 'WhatsApp refused this account connection. Stop sending and request an in-app review if appropriate.';
      this.#pairingRequested = false;
      this.#logger.error('WhatsApp returned forbidden; automatic reconnect is disabled.');
      return;
    }

    const canReconnect = this.#auth.isRegistered || this.#pairingRequested;
    if (canReconnect && RECONNECT_REASONS.has(reason)) {
      this.#scheduleReconnect(reason === DisconnectReason.restartRequired ? 250 : null);
      return;
    }

    this.#status = 'disconnected';
    this.#lastError = `WhatsApp connection closed (reason ${reason}).`;
  }

  #scheduleReconnect(forcedDelayMs = null) {
    if (this.#stopping || this.#reconnectTimer) return;
    this.#reconnectAttempt += 1;
    const ceiling = Math.min(72_000, 3_000 * (2 ** Math.min(this.#reconnectAttempt - 1, 5)));
    const delay = forcedDelayMs ?? crypto.randomInt(3_000, ceiling + 1);
    this.#status = 'reconnecting';
    this.#lastError = `Connection interrupted. Safe reconnect attempt ${this.#reconnectAttempt} is scheduled.`;
    this.#logger.warn({ attempt: this.#reconnectAttempt, delayMs: delay }, 'Scheduling WhatsApp reconnect.');
    this.#reconnectTimer = setTimeout(() => {
      this.#reconnectTimer = null;
      this.connect({ riskAccepted: true, automatic: true }).catch(error => {
        this.#logger.warn({ err: error }, 'WhatsApp reconnect attempt failed.');
        this.#scheduleReconnect();
      });
    }, delay);
    this.#reconnectTimer.unref?.();
  }

  async sendText(toE164, text) {
    if (this.#status !== 'connected' || !this.#socket) {
      throw new GatewayError(
        'session_disconnected',
        'WhatsApp is not connected. The durable outbox will wait for session recovery.',
        { status: 503, retryable: true, retryAfterSeconds: 15 }
      );
    }

    const jid = `${toE164.slice(1)}@s.whatsapp.net`;
    const result = await this.#socket.sendMessage(jid, { text });
    const providerMessageId = result?.key?.id;
    if (!providerMessageId) throw new Error('WhatsApp accepted no message identifier.');
    if (result.message) {
      this.#outgoingMessages.set(providerMessageId, result.message);
      while (this.#outgoingMessages.size > 100) {
        this.#outgoingMessages.delete(this.#outgoingMessages.keys().next().value);
      }
    }
    return providerMessageId;
  }

  async logout() {
    this.#clearReconnectTimer();
    this.#pairingRequested = false;
    const socket = this.#socket;
    this.#socket = null;
    ++this.#generation;

    try {
      if (socket) await withTimeout(socket.logout('SmartSchool administrator unlinked this device'), 10_000, 'WhatsApp logout');
    } catch (error) {
      this.#logger.warn({ err: error }, 'Remote WhatsApp logout failed; local credentials will still be erased.');
      try { socket?.end(new Error('SmartSchool local logout')); } catch {}
    }

    await this.#auth.clear();
    this.#auth = await useEncryptedAuthState(this.#config.authStorePath, this.#config.sessionKey);
    this.#outgoingMessages.clear();
    this.#status = 'disconnected';
    this.#connectedPhone = null;
    this.#qrDataUrl = null;
    this.#qrExpiresAtUtc = null;
    this.#lastError = null;
    this.#reconnectAttempt = 0;
    return this.snapshot();
  }

  async stop() {
    this.#stopping = true;
    this.#clearReconnectTimer();
    ++this.#generation;
    const socket = this.#socket;
    this.#socket = null;
    try { socket?.end(new Error('SmartSchool gateway stopping')); } catch {}
  }

  #clearReconnectTimer() {
    if (this.#reconnectTimer) clearTimeout(this.#reconnectTimer);
    this.#reconnectTimer = null;
  }
}
