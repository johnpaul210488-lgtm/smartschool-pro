import { makeWASocket, initAuthCreds, BufferJSON, proto, DisconnectReason, useMultiFileAuthState } from 'baileys';
import pino from 'pino';
import QRCode from 'qrcode';
import { createHash } from 'node:crypto';

/**
 * Baileys WhatsApp client wrapper.
 * Handles QR pairing, connection lifecycle, message sending, and events.
 */
export function createClient({
  dir,
  makeSocket = makeWASocket,
  logger = pino({ level: 'silent' }),
  reconnectDelay = (attempt) => Math.min(60000, 1000 * 2 ** Math.min(attempt, 6)) + Math.floor(Math.random() * 500),
  sendTimeoutMs = 25000,
  pairingTimeoutMs = 5 * 60 * 1000,
  onStatusChange,
  onQr,
  onMessage,
}) {
  let state = 'off';
  let qr = null;
  let socket = null;
  let generation = 0;
  let qrSequence = 0;
  let retryTimer = null;
  let pairingTimer = null;
  let attempts = 0;
  let pairingUntil = 0;
  let blocked = false;
  let inFlight = false;

  const terminalCodes = new Set([
    DisconnectReason.loggedOut,
    DisconnectReason.forbidden,
    DisconnectReason.connectionReplaced,
    DisconnectReason.badSession,
    DisconnectReason.multideviceMismatch,
  ]);

  const status = () => ({
    state,
    accountFingerprint: fingerprint(),
    qr: !!qr,
    connected: state === 'connected',
  });

  const fingerprint = () => {
    const creds = socket?.auth?.state?.creds;
    if (!creds?.me?.id) return null;
    return createHash('sha256').update(creds.me.id).digest('hex');
  };

  const isLinked = (creds) =>
    typeof creds?.me?.id === 'string' && creds.me.id.length > 0 && !!creds.account;

  const clearRetry = () => {
    if (retryTimer) clearTimeout(retryTimer);
    retryTimer = null;
  };

  const clearPairingTimer = () => {
    if (pairingTimer) clearTimeout(pairingTimer);
    pairingTimer = null;
  };

  function connect(auth) {
    currentAuth = auth;
    const myGeneration = ++generation;
    state = 'connecting';
    qr = null;
    onStatusChange?.(status());

    const current = makeSocket({
      auth: auth.state,
      logger,
      printQRInTerminal: false,
      syncFullHistory: true,
      shouldSyncHistoryMessage: ({ syncType }) =>
        syncType === proto.HistorySync.HistorySyncType.ON_DEMAND ||
        syncType === proto.HistorySync.HistorySyncType.RECENT ||
        syncType === proto.HistorySync.HistorySyncType.INITIAL_BOOTSTRAP,
      emitOwnEvents: false,
      markOnlineOnConnect: false,
      enableRecentMessageCache: false,
      enableAutoSessionRecreation: false,
      maxMsgRetryCount: 0,
      getMessage: async () => undefined,
    });
    socket = current;

    current.ev.on('creds.update', () => {
      if (myGeneration !== generation) return;
      Promise.resolve().then(() => {
        if (myGeneration === generation) auth.saveCreds?.();
      }).catch(() => {
        if (myGeneration !== generation) return;
        blocked = true;
        clearPairingTimer();
        state = 'session_error';
        qr = null;
        onStatusChange?.(status());
        void current.end(new Error('Session persistence failed')).catch(() => {});
      });
    });

    current.ev.on('connection.update', (update) => {
      if (myGeneration !== generation) return;

      if (update.qr && Date.now() < pairingUntil) {
        const sequence = ++qrSequence;
        state = 'awaiting_scan';
        qr = null;
        onStatusChange?.(status());
        QRCode.toDataURL(update.qr, { errorCorrectionLevel: 'M', margin: 3, width: 320 })
          .then((image) => {
            if (myGeneration === generation && sequence === qrSequence && state === 'awaiting_scan') {
              qr = image;
              onQr?.(qr);
              onStatusChange?.(status());
            }
          })
          .catch(() => {
            if (myGeneration !== generation) return;
            blocked = true;
            clearPairingTimer();
            state = 'session_error';
            qr = null;
            onStatusChange?.(status());
            void current.end(new Error('Could not display QR')).catch(() => {});
          });
      }

      if (update.connection === 'open') {
        state = 'connected';
        qr = null;
        attempts = 0;
        pairingUntil = 0;
        clearPairingTimer();
        onStatusChange?.(status());
      }

      if (update.connection !== 'close') return;
      qr = null;
      socket = null;
      if (blocked) {
        state = 'session_error';
        onStatusChange?.(status());
        return;
      }
      const code = update.lastDisconnect?.error?.output?.statusCode;
      if (terminalCodes.has(code)) {
        clearPairingTimer();
        blocked = true;
        state = 'needs_relink';
        onStatusChange?.(status());
        return;
      }
      const mayReconnect = isLinked(auth.state.creds) || Date.now() < pairingUntil;
      if (!mayReconnect) {
        state = 'needs_link';
        onStatusChange?.(status());
        return;
      }
      state = 'reconnecting';
      onStatusChange?.(status());
      const delay =
        code === DisconnectReason.restartRequired ? 500 : reconnectDelay(attempts++);
      retryTimer = setTimeout(() => {
        retryTimer = null;
        if (myGeneration !== generation) return;
        try {
          connect(auth);
        } catch {
          state = 'session_error';
          onStatusChange?.(status());
        }
      }, delay);
      retryTimer.unref?.();
    });

    current.ev.on('messages.upsert', (event) => {
      for (const msg of event.messages) {
        if (msg.key.fromMe) continue;
        onMessage?.(msg);
      }
    });

    current.ev.on('contacts.update', (updates) => {
      for (const u of updates) {
        // contacts are saved by the caller via the REST API / store
      }
    });
  }

  let currentAuth = null;

  function start({ forceNew = false } = {}) {
    if (blocked && !forceNew) {
      return { ok: false, error: 'Unlink/reset the previous session before linking again.' };
    }
    if (socket || retryTimer) {
      return { ok: true, ...status() };
    }
    try {
      // Create auth state using multi-file approach, then adapt to our DB
      const { state: creds, saveCreds } = useMultiFileAuthState(dir);
      const auth = {
        state: { creds, keys: {} },
        saveCreds: async () => {
          await saveCreds();
          // persist to our SQLite via the caller
        },
      };
    } catch {
      // fall through to createAuth below
    }

    // Use provided auth state from caller
    const pairingUntilValue = isLinked(currentAuth?.state?.creds) ? 0 : Date.now() + pairingTimeoutMs;
    pairingUntil = pairingUntilValue;

    if (pairingUntil) {
      clearPairingTimer();
      pairingTimer = setTimeout(() => {
        if (state === 'connected') return;
        ++generation;
        clearRetry();
        const old = socket;
        socket = null;
        qr = null;
        pairingUntil = 0;
        state = 'needs_link';
        onStatusChange?.(status());
        if (old) void old.end(new Error('Pairing expired')).catch(() => {});
      }, pairingTimeoutMs);
      pairingTimer.unref?.();
    }

    // The actual auth object is set by setAuth() before start is called
    if (!currentAuth) {
      return { ok: false, error: 'No auth session configured. Call setAuth() first.' };
    }

    connect(currentAuth);
    return { ok: true, ...status() };
  }

  function setAuth(auth) {
    currentAuth = auth;
  }

  function restore(auth) {
    if (!auth) {
      state = 'needs_link';
      onStatusChange?.(status());
      return;
    }
    if (!isLinked(auth.state.creds)) {
      state = 'needs_link';
      onStatusChange?.(status());
      return;
    }
    currentAuth = auth;
    connect(auth);
  }

  async function unlink() {
    ++generation;
    ++qrSequence;
    clearRetry();
    clearPairingTimer();
    const previous = socket;
    socket = null;
    currentAuth = null;
    qr = null;
    pairingUntil = 0;
    blocked = true;
    state = 'unlinking';
    onStatusChange?.(status());

    let phoneUnlinked = false;
    if (previous) {
      try {
        await Promise.race([
          previous.logout(),
          new Promise((_, reject) => {
            const t = setTimeout(() => reject(new Error('timeout')), 5000);
            t.unref?.();
          }),
        ]);
        phoneUnlinked = true;
      } catch {
        try {
          await previous.end(new Error('Locally unlinked'));
        } catch {}
      }
    }

    blocked = false;
    state = 'off';
    onStatusChange?.(status());
    return { ok: true, phoneUnlinked };
  }

  async function sendText({ phone, text }) {
    if (state !== 'connected' || !socket) {
      return { ok: false, error: 'WhatsApp not connected.' };
    }
    if (inFlight) {
      return { ok: false, error: 'Another message is in progress.' };
    }
    inFlight = true;
    try {
      const response = await Promise.race([
        socket.sendMessage(`${phone.substring(1)}@s.whatsapp.net`, { text }),
        new Promise((_, reject) => {
          const t = setTimeout(() => reject(new Error('timeout')), sendTimeoutMs);
          t.unref?.();
        }),
      ]);
      const id = response?.key?.id;
      return { ok: true, providerMessageId: typeof id === 'string' ? id.slice(0, 100) : null };
    } catch (err) {
      return { ok: false, error: err.message };
    } finally {
      inFlight = false;
    }
  }

  async function sendMedia({ phone, type, path, fileName, caption }) {
    if (state !== 'connected' || !socket) {
      return { ok: false, error: 'WhatsApp not connected.' };
    }
    if (inFlight) {
      return { ok: false, error: 'Another message is in progress.' };
    }
    inFlight = true;
    try {
      const message = {
        [type]: { url: path, fileName, caption },
        caption,
      };
      const response = await Promise.race([
        socket.sendMessage(`${phone.substring(1)}@s.whatsapp.net`, message),
        new Promise((_, reject) => {
          const t = setTimeout(() => reject(new Error('timeout')), sendTimeoutMs);
          t.unref?.();
        }),
      ]);
      const id = response?.key?.id;
      return { ok: true, providerMessageId: typeof id === 'string' ? id.slice(0, 100) : null };
    } catch (err) {
      return { ok: false, error: err.message };
    } finally {
      inFlight = false;
    }
  }

  async function pairingCode({ phoneNumber }) {
    if (state !== 'connected' || !socket) {
      return { ok: false, error: 'WhatsApp not connected.' };
    }
    try {
      const code = await socket.requestPairingCode(phoneNumber);
      return { ok: true, code };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  }

  async function stop() {
    ++generation;
    clearRetry();
    clearPairingTimer();
    const previous = socket;
    socket = null;
    qr = null;
    state = 'off';
    onStatusChange?.(status());
    if (previous) {
      try {
        await previous.end(new Error('Gateway stopping'));
      } catch {}
    }
  }

  return {
    status,
    setAuth,
    getQr: () => qr,
    start,
    restore,
    unlink,
    sendText,
    sendMedia,
    pairingCode,
    stop,
    onStatusChange: (cb) => { onStatusChange = cb; },
    onQr: (cb) => { onQr = cb; },
    onMessage: (cb) => { onMessage = cb; },
  };
}
