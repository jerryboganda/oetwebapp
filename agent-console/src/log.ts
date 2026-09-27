import { pino } from 'pino';
import type { FastifyBaseLogger } from 'fastify';

/** Structured logger used across the sidecar (pino; handed to Fastify as its logger instance). */
export type Logger = FastifyBaseLogger;

// Header names whose values must never reach a log line.
const REDACT_PATHS = [
  'req.headers["x-oet-internal-token"]',
  'req.headers["x-oet-proxy-token"]',
  'req.headers["x-oet-control-token"]',
  'req.headers.authorization',
  'req.headers["proxy-authorization"]',
  'req.headers.cookie',
  '*.token',
  '*.agentToken',
  '*.shipToken',
  '*.nonce',
];

export function createLogger(level = 'info'): Logger {
  return pino({
    level,
    base: { service: 'oet-agent-console' },
    timestamp: pino.stdTimeFunctions.isoTime,
    redact: { paths: REDACT_PATHS, censor: '[REDACTED]' },
  });
}

/** A logger that discards everything (tests, dry runs). */
export function silentLogger(): Logger {
  return pino({ level: 'silent' });
}
