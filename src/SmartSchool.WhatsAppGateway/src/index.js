import pino from 'pino';
import { loadConfig } from './config.js';
import { createGatewayServer } from './http-server.js';
import { MessageService } from './message-service.js';
import { ReceiptLedger } from './receipt-ledger.js';
import { SafetyGate } from './safety-gate.js';
import { WhatsAppSessionManager } from './session-manager.js';

const logger = pino({
  level: process.env.SMARTSCHOOL_WHATSAPP_LOG_LEVEL || 'info',
  base: { service: 'smartschool-whatsapp-gateway' },
  redact: {
    paths: [
      'apiKey',
      'sessionKey',
      '*.apiKey',
      '*.sessionKey',
      'qr',
      '*.qr',
      'text',
      '*.text',
      'message',
      '*.message'
    ],
    censor: '[REDACTED]'
  }
});

let server;
let session;
let shuttingDown = false;

async function main() {
  const config = await loadConfig();
  const ledger = await new ReceiptLedger(config.receiptStorePath, config.sessionKey).initialize();
  const gate = new SafetyGate({
    minIntervalMs: config.minSendIntervalMs,
    maxIntervalMs: config.maxSendIntervalMs,
    maxPending: config.maxPendingRequests
  });
  session = await new WhatsAppSessionManager(config, logger).initialize();
  const messages = new MessageService({ config, session, ledger, gate, logger });
  server = createGatewayServer({ config, session, messages, logger });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(config.port, config.host, resolve);
  });
  logger.info(
    {
      host: config.host,
      port: config.port,
      stateDirectory: config.stateDirectory,
      minSendIntervalMs: config.minSendIntervalMs,
      maxSendIntervalMs: config.maxSendIntervalMs,
      maxMessagesPerDay: config.maxMessagesPerDay
    },
    'Linked-device gateway listening on loopback.'
  );
}

async function shutdown(signal) {
  if (shuttingDown) return;
  shuttingDown = true;
  logger.info({ signal }, 'Gateway shutdown requested.');
  const forceExit = setTimeout(() => process.exit(1), 10_000);
  forceExit.unref();
  try {
    if (server) await new Promise(resolve => server.close(resolve));
    await session?.stop();
    clearTimeout(forceExit);
    process.exit(0);
  } catch (error) {
    logger.error({ err: error }, 'Gateway shutdown failed.');
    process.exit(1);
  }
}

process.on('SIGINT', () => { void shutdown('SIGINT'); });
process.on('SIGTERM', () => { void shutdown('SIGTERM'); });
process.on('uncaughtException', error => {
  logger.fatal({ err: error }, 'Uncaught gateway exception.');
  void shutdown('uncaughtException');
});
process.on('unhandledRejection', error => {
  logger.fatal({ err: error }, 'Unhandled gateway rejection.');
  void shutdown('unhandledRejection');
});

main().catch(error => {
  logger.fatal({ err: error }, 'Gateway failed to start.');
  process.exitCode = 1;
});
