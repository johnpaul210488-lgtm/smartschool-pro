import crypto from 'node:crypto';
import http from 'node:http';
import { GatewayError, publicError } from './errors.js';

function secureEquals(left, right) {
  const a = Buffer.from(left || '', 'utf8');
  const b = Buffer.from(right || '', 'utf8');
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}

function sendJson(response, status, body) {
  const payload = Buffer.from(JSON.stringify(body));
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': payload.length,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer'
  });
  response.end(payload);
}

async function readJson(request, limitBytes) {
  const contentType = request.headers['content-type'] || '';
  if (!contentType.toLowerCase().startsWith('application/json')) {
    throw new GatewayError('unsupported_media_type', 'Content-Type must be application/json.', {
      status: 415
    });
  }

  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > limitBytes) {
      throw new GatewayError('request_too_large', 'Request body is too large.', { status: 413 });
    }
    chunks.push(chunk);
  }

  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
  } catch {
    throw new GatewayError('invalid_json', 'Request body is not valid JSON.');
  }
}

export function createGatewayServer({ config, session, messages, logger }) {
  const server = http.createServer(async (request, response) => {
    try {
      const url = new URL(request.url || '/', `http://${request.headers.host || 'localhost'}`);

      if (request.method === 'GET' && url.pathname === '/health') {
        const snapshot = session.snapshot();
        sendJson(response, 200, {
          status: 'ok',
          sessionStatus: snapshot.status,
          version: process.env.npm_package_version || '1.0.0'
        });
        return;
      }

      const authorization = request.headers.authorization || '';
      const expected = `Bearer ${config.apiKey}`;
      if (!secureEquals(authorization, expected)) {
        response.setHeader('WWW-Authenticate', 'Bearer');
        sendJson(response, 401, {
          success: false,
          error: { code: 'unauthorized', message: 'A valid gateway bearer token is required.', retryable: false }
        });
        return;
      }

      if (request.method === 'GET' && url.pathname === '/v1/session') {
        sendJson(response, 200, { success: true, data: messages.status() });
        return;
      }

      if (request.method === 'POST' && url.pathname === '/v1/session/connect') {
        const body = await readJson(request, config.requestBodyLimitBytes);
        const data = await session.connect({ riskAccepted: body.acceptTermsAndRisk === true });
        sendJson(response, 202, { success: true, data, message: 'WhatsApp connection started.' });
        return;
      }

      if (request.method === 'POST' && url.pathname === '/v1/session/logout') {
        const body = await readJson(request, config.requestBodyLimitBytes);
        if (body.confirmUnlink !== true) {
          throw new GatewayError(
            'unlink_confirmation_required',
            'Set confirmUnlink=true to erase the linked-device session.',
            { status: 428 }
          );
        }
        const data = await session.logout();
        sendJson(response, 200, { success: true, data, message: 'WhatsApp device unlinked and local credentials erased.' });
        return;
      }

      if (request.method === 'POST' && url.pathname === '/v1/messages') {
        const body = await readJson(request, config.requestBodyLimitBytes);
        const data = await messages.send(body);
        sendJson(response, data.duplicate ? 200 : 202, {
          success: true,
          data,
          message: data.duplicate ? 'Original send receipt returned.' : 'Message accepted by WhatsApp.'
        });
        return;
      }

      sendJson(response, 404, {
        success: false,
        error: { code: 'not_found', message: 'Endpoint not found.', retryable: false }
      });
    } catch (error) {
      const mapped = publicError(error);
      if (mapped.status >= 500) logger.error({ err: error }, 'Gateway request failed.');
      else logger.warn({ code: mapped.body.error.code }, 'Gateway request rejected.');
      if (!response.headersSent) sendJson(response, mapped.status, mapped.body);
      else response.destroy();
    }
  });

  server.requestTimeout = 120_000;
  server.headersTimeout = 15_000;
  server.keepAliveTimeout = 5_000;
  server.maxConnections = 150;
  return server;
}

export const httpServerInternals = Object.freeze({ secureEquals, readJson });
