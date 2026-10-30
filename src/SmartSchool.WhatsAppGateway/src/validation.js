import { GatewayError } from './errors.js';

export function normalizePhone(raw) {
  if (typeof raw !== 'string' || raw.trim() === '') {
    throw new GatewayError('invalid_recipient', 'A recipient phone number is required.');
  }
  if (raw.includes('@') || /g\.us|broadcast/i.test(raw)) {
    throw new GatewayError('invalid_recipient', 'Only individual phone numbers are allowed.');
  }

  let digits = raw.replace(/^whatsapp:/i, '').replace(/\D/g, '');
  if (digits.startsWith('00')) digits = digits.slice(2);
  if (digits.length === 11 && digits.startsWith('0')) digits = `92${digits.slice(1)}`;
  else if (digits.length === 10 && digits.startsWith('3')) digits = `92${digits}`;

  if (digits.length < 10 || digits.length > 15 || digits.startsWith('0')) {
    throw new GatewayError('invalid_recipient', 'The recipient must be a valid E.164 phone number.');
  }
  return `+${digits}`;
}

export function validateSendRequest(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) {
    throw new GatewayError('invalid_request', 'A JSON request body is required.');
  }
  const to = normalizePhone(input.to);
  const text = typeof input.text === 'string' ? input.text.trim() : '';
  if (text.length === 0 || text.length > 4_096) {
    throw new GatewayError('invalid_message', 'Message text must contain between 1 and 4096 characters.');
  }
  const idempotencyKey = typeof input.idempotencyKey === 'string'
    ? input.idempotencyKey.trim()
    : '';
  if (!/^[A-Za-z0-9_-]{16,128}$/.test(idempotencyKey)) {
    throw new GatewayError(
      'invalid_idempotency_key',
      'idempotencyKey must be 16-128 URL-safe characters.'
    );
  }
  if (input.classification !== 'transactional') {
    throw new GatewayError(
      'unsupported_message_classification',
      'This gateway accepts consented transactional school alerts only; bulk or marketing sends are disabled.'
    );
  }
  return { to, text, idempotencyKey, classification: 'transactional' };
}
