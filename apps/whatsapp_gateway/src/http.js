import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';

const MAX_BODY = 4096;
function authorized(header, secret) {
  if (typeof header !== 'string' || !header.startsWith('Bearer ')) return false;
  const supplied = Buffer.from(header.slice(7));
  const expected = Buffer.from(secret);
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
}
function respond(res, code, body) {
  res.writeHead(code, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store, private',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(JSON.stringify(body));
}
async function readJson(req) {
  if (!/^application\/json(?:\s*;|$)/i.test(req.headers['content-type'] ?? '')) throw new Error('content type');
  const chunks = [];
  let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > MAX_BODY) throw new Error('too large');
    chunks.push(chunk);
  }
  const body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new Error('invalid body');
  return body;
}

export function createGatewayServer({ session, secret }) {
  if (typeof secret !== 'string' || secret.length < 32) throw new Error('A strong gateway secret is required.');
  const server = http.createServer(async (req, res) => {
    // The server is bound to IPv4 loopback only. Reject browser-origin requests
    // as a second layer; the random secret is never made available to a browser.
    if (req.headers.origin || !authorized(req.headers.authorization, secret)) {
      respond(res, 401, { ok: false, error: 'Unauthorized.' });
      return;
    }
    try {
      const url = new URL(req.url, 'http://127.0.0.1');
      if (req.method === 'GET' && url.pathname === '/status') return respond(res, 200, { ok: true, ...session.status() });
      if (req.method === 'GET' && url.pathname === '/qr') return respond(res, 200, { ok: true, qrDataUrl: session.getQr() });
      if (req.method === 'POST' && url.pathname === '/session/start') {
        const body = await readJson(req);
        if (body.riskConfirmed !== true) return respond(res, 400, { ok: false, error: 'Test risk confirmation is required.' });
        return respond(res, 200, session.start());
      }
      if (req.method === 'POST' && url.pathname === '/session/unlink') {
        const body = await readJson(req);
        if (body.confirm !== true) return respond(res, 400, { ok: false, error: 'Unlink confirmation is required.' });
        return respond(res, 200, await session.unlink());
      }
      if (req.method === 'POST' && url.pathname === '/test/send') {
        const body = await readJson(req);
        return respond(res, 200, await session.sendTest(body));
      }
      return respond(res, 404, { ok: false, error: 'Not found.' });
    } catch {
      if (!res.headersSent) respond(res, 400, { ok: false, error: 'Invalid request.' });
      else res.end();
    }
  });
  server.requestTimeout = 30000;
  server.headersTimeout = 10000;
  return server;
}
