export class GatewayError extends Error {
  constructor(code, message, { status = 400, retryable = false, retryAfterSeconds = null } = {}) {
    super(message);
    this.name = 'GatewayError';
    this.code = code;
    this.status = status;
    this.retryable = retryable;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export function publicError(error) {
  if (error instanceof GatewayError) {
    return {
      status: error.status,
      body: {
        success: false,
        error: {
          code: error.code,
          message: error.message,
          retryable: error.retryable,
          retryAfterSeconds: error.retryAfterSeconds
        }
      }
    };
  }

  return {
    status: 500,
    body: {
      success: false,
      error: {
        code: 'internal_error',
        message: 'The linked-device gateway encountered an internal error.',
        retryable: false,
        retryAfterSeconds: null
      }
    }
  };
}
