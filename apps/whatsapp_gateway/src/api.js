import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { randomUUID } from 'node:crypto';

/**
 * REST API + SSE for the WhatsApp service.
 * Auth: Bearer token from SMARTSCHOOL_WHATSAPP_SECRET env var.
 */

const MAX_BODY = 200000; // 200KB for media metadata
const E164 = /^\+[1-9][0-9]{7,14}$/;

function authorized(header, secret) {
  if (typeof header !== 'string' || !header.startsWith('Bearer ')) return false;
  const supplied = Buffer.from(header.slice(7));
  const expected = Buffer.from(secret);
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
}

function respond(res, code, body) {
  const payload = typeof body === 'string' ? body : JSON.stringify(body);
  res.writeHead(code, {
    'Content-Type': typeof body === 'string' ? 'text/plain; charset=utf-8' : 'application/json; charset=utf-8',
    'Cache-Control': 'no-store, private',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(payload);
}

async function readJson(req) {
  if (!/^application\/json(?:;\s*|$)/i.test(req.headers['content-type'] ?? '')) throw new Error('content type');
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

export function createApi({ client, queue, db, secret }) {
  if (typeof secret !== 'string' || secret.length < 16) throw new Error('A strong API secret is required.');

  const sseClients = new Set();

  function broadcast(event, data) {
    const payload = `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
    for (const c of sseClients) {
      try {
        c.write(payload);
      } catch {
        sseClients.delete(c);
      }
    }
  }

  const server = http.createServer(async (req, res) => {
    // CORS preflight
    if (req.method === 'OPTIONS') {
      res.writeHead(204, {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Authorization, Content-Type',
        'Access-Control-Max-Age': '86400',
      });
      res.end();
      return;
    }

    // Auth check (except SSE which uses query param)
    const isSSE = req.headers.accept?.includes('text/event-stream');
    const authHeader = req.headers.authorization || (isSSE ? (new URL(req.url, 'http://127.0.0.1')).searchParams.get('token') : '');
    if (!authorized(authHeader, secret)) {
      if (isSSE) {
        res.writeHead(401, { 'Content-Type': 'text/plain' });
        res.end('Unauthorized');
        return;
      }
      respond(res, 401, { ok: false, error: 'Unauthorized.' });
      return;
    }

    try {
      const url = new URL(req.url, 'http://127.0.0.1');
      const path = url.pathname;

      // ---------- SSE events ----------
      if (req.method === 'GET' && path === '/api/events') {
        res.writeHead(200, {
          'Content-Type': 'text/event-stream',
          'Cache-Control': 'no-cache',
          Connection: 'keep-alive',
          'X-Accel-Buffering': 'no',
        });
        res.write(': connected\n\n');
        sseClients.add(res);
        req.on('close', () => sseClients.delete(res));
        return;
      }

      // ---------- Status ----------
      if (req.method === 'GET' && path === '/api/status') {
        const st = client.status();
        const q = queue.getStatus();
        const counters = db.getDailyCounter({ date: new Date().toISOString().slice(0, 10) });
        return respond(res, 200, {
          ok: true,
          ...st,
          queue: q,
          daily: counters,
        });
      }

      // ---------- QR ----------
      if (req.method === 'GET' && path === '/api/session/qr') {
        return respond(res, 200, { ok: true, qrDataUrl: client.getQr?.() ?? null });
      }

      // ---------- Connect ----------
      if (req.method === 'POST' && path === '/api/session/connect') {
        const result = client.start();
        if (result.ok) broadcast('connection-changed', { state: result.state });
        return respond(res, 200, result);
      }

      // ---------- Logout / Unlink ----------
      if (req.method === 'POST' && path === '/api/session/logout') {
        const result = await client.unlink();
        broadcast('connection-changed', { state: 'off' });
        return respond(res, 200, result);
      }

      // ---------- Pairing code ----------
      if (req.method === 'POST' && path === '/api/session/pairing-code') {
        const body = await readJson(req);
        if (typeof body.phoneNumber !== 'string' || !E164.test(body.phoneNumber)) {
          return respond(res, 400, { ok: false, error: 'Valid E.164 phone number required.' });
        }
        const result = await client.pairingCode({ phoneNumber: body.phoneNumber });
        return respond(res, 200, result);
      }

      // ---------- Send text ----------
      if (req.method === 'POST' && path === '/api/messages/text') {
        const body = await readJson(req);
        if (typeof body.to !== 'string' || !E164.test(body.to)) {
          return respond(res, 400, { ok: false, error: 'Valid E.164 phone number required.' });
        }
        if (typeof body.text !== 'string' || !body.text.trim()) {
          return respond(res, 400, { ok: false, error: 'Text is required.' });
        }
        const priority = typeof body.priority === 'number' ? body.priority : 10;
        const id = randomUUID();
        db.enqueueOutbox({
          id,
          phone: body.to,
          type: 'text',
          text: body.text.trim(),
          priority,
          next_attempt_at: new Date().toISOString(),
        });
        queue.processBatch();
        return respond(res, 202, { ok: true, id, status: 'queued', priority });
      }

      // ---------- Send media ----------
      if (req.method === 'POST' && path === '/api/messages/media') {
        const body = await readJson(req);
        if (typeof body.to !== 'string' || !E164.test(body.to)) {
          return respond(res, 400, { ok: false, error: 'Valid E.164 phone number required.' });
        }
        if (!body.path && !body.url) {
          return respond(res, 400, { ok: false, error: 'Media path or URL is required.' });
        }
        const type = body.type || 'document';
        if (!['image', 'video', 'document', 'audio'].includes(type)) {
          return respond(res, 400, { ok: false, error: 'Invalid media type.' });
        }
        const priority = typeof body.priority === 'number' ? body.priority : 10;
        const id = randomUUID();
        db.enqueueOutbox({
          id,
          phone: body.to,
          type: 'media',
          text: body.caption,
          media_path: body.path || body.url,
          media_type: type,
          file_name: body.file_name,
          caption: body.caption,
          priority,
          next_attempt_at: new Date().toISOString(),
        });
        queue.processBatch();
        return respond(res, 202, { ok: true, id, status: 'queued', priority });
      }

      // ---------- Send bulk ----------
      if (req.method === 'POST' && path === '/api/messages/bulk') {
        const body = await readJson(req);
        if (!Array.isArray(body.items) || body.items.length === 0) {
          return respond(res, 400, { ok: false, error: 'Items array is required.' });
        }
        const ids = [];
        const now = new Date().toISOString();
        for (const item of body.items) {
          if (typeof item.to !== 'string' || !E164.test(item.to)) continue;
          if (typeof item.text !== 'string' || !item.text.trim()) continue;
          const id = randomUUID();
          const priority = typeof item.priority === 'number' ? item.priority : 10;
          db.enqueueOutbox({
            id,
            phone: item.to,
            type: 'text',
            text: item.text.trim(),
            priority,
            next_attempt_at: now,
          });
          ids.push(id);
        }
        queue.processBatch();
        return respond(res, 202, { ok: true, count: ids.length, ids });
      }

      // ---------- Queue ----------
      if (req.method === 'GET' && path === '/api/queue') {
        const statusFilter = url.searchParams.get('status');
        const count = db.outboxCount({ status: statusFilter || undefined });
        return respond(res, 200, { ok: true, count, status: queue.getStatus() });
      }

      // ---------- Contacts ----------
      if (req.method === 'GET' && path === '/api/contacts') {
        const search = url.searchParams.get('search') || undefined;
        const limit = parseInt(url.searchParams.get('limit') || '50', 10);
        const offset = parseInt(url.searchParams.get('offset') || '0', 10);
        const contacts = db.listContacts({ search, limit, offset });
        return respond(res, 200, { ok: true, contacts });
      }

      // ---------- Messages ----------
      if (req.method === 'GET' && path === '/api/messages') {
        const contactJid = url.searchParams.get('contactJid') || undefined;
        const limit = parseInt(url.searchParams.get('limit') || '50', 10);
        const offset = parseInt(url.searchParams.get('offset') || '0', 10);
        const messages = db.listMessages({ contactJid, limit, offset });
        return respond(res, 200, { ok: true, messages });
      }

      // ---------- 404 ----------
      return respond(res, 404, { ok: false, error: 'Not found.' });
    } catch (err) {
      if (!res.headersSent) respond(res, 400, { ok: false, error: 'Invalid request.' });
      else res.end();
    }
  });

  server.requestTimeout = 30000;
  server.headersTimeout = 10000;
  return server;
}
