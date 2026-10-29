import { randomInt } from 'node:crypto';

// Only synthetic messages live here. No school queue, student data, inbound
// messages or arbitrary text can reach this test gateway.
const samples = {
  pick: '🚌 TEST ONLY — SmartSchool\n\n✅ Sample Student (Class TEST) has BOARDED Bus #TEST.\n\nThis is a test, not a real bus event.',
  drop: '🚌 TEST ONLY — SmartSchool\n\n🏠 Sample Student (Class TEST) has been DROPPED OFF safely.\n\nThis is a test, not a real bus event.',
};
const E164 = /^\+[1-9][0-9]{7,14}$/;
const requestIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function createSession({ dir, createAuth, hasSession, deleteSession, makeSocket, renderQr,
  logger, proto, disconnectReason, reconnectDelay = (attempt) => Math.min(60000, 1000 * 2 ** Math.min(attempt, 6)) + randomInt(500),
  sendTimeoutMs = 25000, pairingTimeoutMs = 5 * 60 * 1000 }) {
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
  let recentSends = [];
  const requests = new Map();

  const status = () => ({ state, testOnly: true });
  // For Baileys' QR pairing, `registered` can remain false even after pairing;
  // the successful QR update persists `me.id` and `account` instead.
  const isLinked = (creds) => typeof creds?.me?.id === 'string' && creds.me.id.length > 0 && !!creds.account;
  const clearRetry = () => { if (retryTimer) clearTimeout(retryTimer); retryTimer = null; };
  const clearPairingTimer = () => { if (pairingTimer) clearTimeout(pairingTimer); pairingTimer = null; };
  const terminalCodes = new Set([
    disconnectReason.loggedOut, disconnectReason.forbidden, disconnectReason.connectionReplaced,
    disconnectReason.badSession, disconnectReason.multideviceMismatch,
  ]);

  function connect(auth) {
    const myGeneration = ++generation;
    state = 'connecting';
    qr = null;
    const current = makeSocket({
      auth: auth.state,
      logger,
      printQRInTerminal: false,
      syncFullHistory: false,
      // Bootstrap is needed for WhatsApp's LID mappings; no history is persisted
      // or subscribed to by this gateway, and recent/full history is not requested.
      shouldSyncHistoryMessage: ({ syncType }) => syncType === proto.HistorySync.HistorySyncType.INITIAL_BOOTSTRAP,
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
        if (myGeneration === generation) return auth.saveCreds();
      }).catch(() => {
        if (myGeneration !== generation) return;
        blocked = true;
        clearPairingTimer();
        state = 'session_error';
        qr = null;
        void current.end(new Error('Session persistence failed')).catch(() => {});
      });
    });
    current.ev.on('connection.update', (update) => {
      if (myGeneration !== generation) return;
      if (update.qr && Date.now() < pairingUntil) {
        const sequence = ++qrSequence;
        state = 'awaiting_scan';
        void renderQr(update.qr).then((image) => {
          if (myGeneration === generation && sequence === qrSequence && state === 'awaiting_scan') qr = image;
        }).catch(() => {
          if (myGeneration !== generation) return;
          blocked = true;
          clearPairingTimer();
          state = 'session_error';
          qr = null;
          void current.end(new Error('Could not display test QR')).catch(() => {});
        });
      }
      if (update.connection === 'open') {
        state = 'connected';
        qr = null;
        attempts = 0;
        pairingUntil = 0;
        clearPairingTimer();
      }
      if (update.connection !== 'close') return;
      qr = null;
      socket = null;
      if (blocked) { state = 'session_error'; return; }
      const code = update.lastDisconnect?.error?.output?.statusCode;
      if (terminalCodes.has(code)) {
        clearPairingTimer();
        blocked = true;
        state = 'needs_relink';
        return;
      }
      const mayReconnect = isLinked(auth.state.creds) || Date.now() < pairingUntil;
      if (!mayReconnect) { state = 'needs_link'; return; }
      state = 'reconnecting';
      const delay = code === disconnectReason.restartRequired ? 500 : reconnectDelay(attempts++);
      retryTimer = setTimeout(() => {
        retryTimer = null;
        if (myGeneration !== generation) return;
        try { connect(auth); } catch { state = 'session_error'; }
      }, delay);
      retryTimer.unref?.();
    });
  }

  function start() {
    if (blocked) return { ok: false, error: 'Unlink/reset the previous session before linking again.' };
    if (socket || retryTimer) return { ok: true, ...status() };
    try {
      const auth = createAuth(dir);
      // The user has explicitly started pairing. Expire an unattended QR after
      // five minutes; reconnect existing registered sessions without a new QR.
      pairingUntil = isLinked(auth.state.creds) ? 0 : Date.now() + pairingTimeoutMs;
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
          if (old) void old.end(new Error('Pairing expired')).catch(() => {});
        }, pairingTimeoutMs);
        pairingTimer.unref?.();
      }
      connect(auth);
      return { ok: true, ...status() };
    } catch {
      clearPairingTimer();
      state = 'session_error';
      return { ok: false, error: 'Unable to start the encrypted test session.' };
    }
  }

  function restore() {
    if (!hasSession(dir)) return;
    try {
      const auth = createAuth(dir);
      if (!isLinked(auth.state.creds)) { state = 'needs_link'; return; }
      connect(auth);
    } catch { blocked = true; state = 'session_error'; }
  }

  async function unlink() {
    ++generation;
    ++qrSequence;
    clearRetry();
    clearPairingTimer();
    const previous = socket;
    socket = null;
    qr = null;
    pairingUntil = 0;
    blocked = true;
    state = 'unlinking';
    let phoneUnlinked = false;
    if (previous) {
      try {
        await Promise.race([
          previous.logout(),
          new Promise((_, reject) => { const t = setTimeout(() => reject(new Error('timeout')), 5000); t.unref?.(); }),
        ]);
        phoneUnlinked = true;
      } catch { try { await previous.end(new Error('Locally unlinked')); } catch {} }
    }
    try {
      deleteSession(dir);
      blocked = false;
      state = 'off';
      return { ok: true, phoneUnlinked };
    } catch {
      state = 'session_error';
      return { ok: false, error: 'Could not remove the local session.' };
    }
  }

  async function sendTest({ phone, type, consentConfirmed, requestId }) {
    if (consentConfirmed !== true || typeof phone !== 'string' || !E164.test(phone) ||
        !Object.hasOwn(samples, type) || !requestIdPattern.test(requestId ?? '')) {
      return { ok: false, result: 'not_sent', error: 'Provide a valid test number, sample and consent.' };
    }
    // An identical ID in the same running process is never sent a second time,
    // including while its first outcome is unknown. No automatic retry is made.
    if (requests.has(requestId)) return requests.get(requestId);
    if (state !== 'connected' || !socket) return { ok: false, result: 'not_sent', error: 'Test account is not connected.' };
    if (inFlight) return { ok: false, result: 'not_sent', error: 'Another test is already in progress.' };
    recentSends = recentSends.filter(t => Date.now() - t < 60 * 60 * 1000);
    if (recentSends.length >= 10) return { ok: false, result: 'not_sent', error: 'Test limit reached. Try again later.' };
    if (requests.size >= 100) requests.delete(requests.keys().next().value);
    inFlight = true;
    recentSends.push(Date.now());
    const pending = (async () => {
      try {
        // This acknowledgement is NOT proof of delivery. A timeout or failure
        // after the send begins is ambiguous and must not be retried blindly.
        await Promise.race([
          socket.sendMessage(`${phone.substring(1)}@s.whatsapp.net`, { text: samples[type] }),
          new Promise((_, reject) => { const t = setTimeout(() => reject(new Error('timeout')), sendTimeoutMs); t.unref?.(); }),
        ]);
        return { ok: true, result: 'submitted' };
      } catch {
        return { ok: false, result: 'unknown', error: 'Outcome unknown. Check the test phone before trying again.' };
      } finally { inFlight = false; }
    })();
    requests.set(requestId, pending);
    return pending;
  }

  async function stop() {
    ++generation;
    clearRetry();
    clearPairingTimer();
    const previous = socket;
    socket = null;
    qr = null;
    if (previous) { try { await previous.end(new Error('Gateway stopping')); } catch {} }
  }

  return { status, getQr: () => qr, start, restore, unlink, sendTest, stop };
}
