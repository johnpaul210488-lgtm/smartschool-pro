/**
 * Persistent priority queue processor for WhatsApp messages.
 *
 * Vyapar-style policy:
 *  - HIGH priority (1-5): batch up to 5, send in parallel (transactional: invoices, replies)
 *  - LOW priority (6+): 1 message at a time, 4.5s gap (marketing: reminders, promos)
 *  - Daily marketing cap: 300/day
 *  - Retry: 2 attempts, 120s gap
 */

export function createQueue({
  db,
  sendText,
  sendMedia,
  getDailyCounter,
  incrementDailyCounter,
  logger,
  onMessageStatus, // callback from client when message status changes
}) {
  const MARKETING_DAILY_LIMIT = 300;
  const LOW_PRIORITY_GAP_MS = 4500;
  const HIGH_PRIORITY_BATCH = 5;
  const RETRY_MAX = 2;
  const RETRY_GAP_MS = 120000;

  let running = false;
  let processorTimer = null;
  let lastLowSend = 0;
  let highBatchInFlight = 0;
  let paused = false;

  const dateKey = () => new Date().toISOString().slice(0, 10);

  function isMarketing(priority) {
    return priority >= 6;
  }

  async function canSendMarketing() {
    const counter = getDailyCounter({ date: dateKey() });
    return counter.marketing_count < MARKETING_DAILY_LIMIT;
  }

  async function processOne(item) {
    const isMkt = isMarketing(item.priority);

    if (isMkt) {
      if (!(await canSendMarketing())) {
        // Defer to tomorrow
        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        tomorrow.setHours(0, 0, 0, 0);
        db.markOutboxFailed(item.id, 'Daily marketing limit reached', tomorrow.toISOString());
        logger.warn({ id: item.id, phone: item.phone }, 'Marketing daily limit reached');
        return;
      }
    }

    db.markOutboxSending(item.id);

    try {
      let result;
      if (item.type === 'media') {
        result = await sendMedia({
          phone: item.phone,
          type: item.media_type || 'document',
          path: item.media_path,
          fileName: item.file_name,
          caption: item.caption || item.text,
        });
      } else {
        result = await sendText({ phone: item.phone, text: item.text || '' });
      }

      db.markOutboxSent(item.id);
      if (isMkt) incrementDailyCounter({ date: dateKey(), type: 'marketing' });
      else incrementDailyCounter({ date: dateKey(), type: 'transactional' });
      logger.info({ id: item.id, phone: item.phone, priority: item.priority }, 'Message sent');
    } catch (err) {
      const nextAttempt =
        item.attempts + 1 < RETRY_MAX
          ? new Date(Date.now() + RETRY_GAP_MS).toISOString()
          : null;
      db.markOutboxFailed(item.id, err.message, nextAttempt);
      logger.warn({ id: item.id, phone: item.phone, err: err.message }, 'Message send failed');
    }
  }

  // Handle message status updates from WhatsApp (sent/delivered/read)
  function handleMessageStatus(update) {
    // Map WhatsApp status to our outbox status if we can track by providerMessageId
    // This is best-effort; the outbox item id is separate from WhatsApp message id
    logger.debug({ update }, 'Message status update received');
  }

  async function processBatch() {
    if (running || paused) return;
    running = true;

    try {
      const now = new Date().toISOString();

      // --- HIGH priority: batch up to 5, parallel ---
      const highItems = db.pendingOutbox({
        limit: HIGH_PRIORITY_BATCH,
      }).filter((i) => i.priority <= 5);

      if (highItems.length > 0) {
        highBatchInFlight = highItems.length;
        await Promise.all(highItems.map((i) => processOne(i)));
        highBatchInFlight = 0;
      }

      // --- LOW priority: one at a time with 4.5s gap ---
      const nowTs = Date.now();
      if (nowTs - lastLowSend >= LOW_PRIORITY_GAP_MS || lastLowSend === 0) {
        const lowItems = db.pendingOutbox({ limit: 1 }).filter((i) => i.priority >= 6);
        if (lowItems.length > 0) {
          lastLowSend = nowTs;
          await processOne(lowItems[0]);
        }
      }
    } finally {
      running = false;
    }
  }

  function start(intervalMs = 1000) {
    if (processorTimer) return;
    processBatch(); // immediate first run
    processorTimer = setInterval(processBatch, intervalMs);
    processorTimer.unref?.();
  }

  function stop() {
    if (processorTimer) {
      clearInterval(processorTimer);
      processorTimer = null;
    }
  }

  function pause() {
    paused = true;
  }

  function resume() {
    paused = false;
    processBatch();
  }

  function getStatus() {
    return {
      running,
      paused,
      highBatchInFlight,
      lastLowSend,
      pendingCount: db.outboxCount({ status: 'pending' }),
      sendingCount: db.outboxCount({ status: 'sending' }),
      sentCount: db.outboxCount({ status: 'sent' }),
      failedCount: db.outboxCount({ status: 'failed' }),
      marketingToday: getDailyCounter({ date: dateKey() }).marketing_count,
      marketingLimit: MARKETING_DAILY_LIMIT,
    };
  }

  return { start, stop, pause, resume, getStatus, processBatch, handleMessageStatus };
}
