import path from 'node:path';
import { makeWASocket, initAuthCreds, BufferJSON, proto, DisconnectReason } from 'baileys';
import pino from 'pino';
import QRCode from 'qrcode';
import { createEncryptedAuth, hasSavedSession, deleteSavedSession } from './encrypted-auth.js';
import { createGatewayServer } from './http.js';
import { createSession } from './session.js';

const secret = process.env.SMARTSCHOOL_QR_SECRET;
const dir = process.env.SMARTSCHOOL_QR_STATE_DIR;
const parentPid = Number(process.env.SMARTSCHOOL_QR_PARENT_PID);
delete process.env.SMARTSCHOOL_QR_SECRET;
if (!secret || secret.length < 32 || !dir || !path.isAbsolute(dir) || !Number.isInteger(parentPid) || parentPid < 1) {
  process.stderr.write('Gateway configuration is invalid.\n');
  process.exit(1);
}
const session = createSession({
  dir,
  createAuth: (d) => createEncryptedAuth({ dir: d, initCreds: initAuthCreds, BufferJSON,
    appStateFromObject: (value) => proto.Message.AppStateSyncKeyData.fromObject(value) }),
  hasSession: hasSavedSession,
  deleteSession: deleteSavedSession,
  makeSocket: makeWASocket,
  renderQr: (qr) => QRCode.toDataURL(qr, { errorCorrectionLevel: 'M', margin: 3, width: 320 }),
  logger: pino({ level: 'silent' }),
  proto,
  disconnectReason: DisconnectReason,
});
const server = createGatewayServer({ session, secret });
server.on('error', () => { process.stderr.write('Gateway failed to start.\n'); process.exit(1); });
server.listen(0, '127.0.0.1', () => {
  process.stdout.write(`READY ${server.address().port}\n`);
  // Restore only already-registered credentials. Never open a new QR without
  // an authenticated administrator explicitly requesting it.
  session.restore();
});
async function shutdown() {
  server.close();
  await session.stop();
  process.exit(0);
}
process.on('SIGTERM', () => { void shutdown(); });
process.on('SIGINT', () => { void shutdown(); });
// Do not leave an authenticated gateway/session running if its API parent dies.
setInterval(() => {
  try { process.kill(parentPid, 0); } catch { void shutdown(); }
}, 3000).unref();
