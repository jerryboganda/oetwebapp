// HTTP error type shared by every route handler. The server's error handler
// turns it into the CONTRACT.md §3 envelope: { error: { code, message } }.

export class HttpError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = 'HttpError';
    this.status = status;
    this.code = code;
  }
}

export interface ErrorEnvelope {
  error: { code: string; message: string };
}

export function errorEnvelope(code: string, message: string): ErrorEnvelope {
  return { error: { code, message } };
}

export const badRequest = (code: string, message: string): HttpError => new HttpError(400, code, message);
export const unauthorized = (message = 'Missing or invalid internal token.'): HttpError =>
  new HttpError(401, 'unauthorized', message);
export const forbidden = (code: string, message: string): HttpError => new HttpError(403, code, message);
export const notFound = (code: string, message: string): HttpError => new HttpError(404, code, message);
export const conflict = (code: string, message: string): HttpError => new HttpError(409, code, message);
/** 423: the console is killed or draining, or the owner lease has lapsed. */
export const locked = (code: string, message: string): HttpError => new HttpError(423, code, message);
export const tooManyTurns = (message: string): HttpError => new HttpError(429, 'concurrency_limit', message);

export function isHttpError(value: unknown): value is HttpError {
  return value instanceof HttpError;
}

/** Message of an unknown thrown value, never including a stack. */
export function describeError(value: unknown): string {
  if (value instanceof Error) return value.message;
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}
