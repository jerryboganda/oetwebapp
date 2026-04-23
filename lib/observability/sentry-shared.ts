// Shared Sentry helpers used by all runtimes (client, server/node, edge).
//
// Privacy contract mirrors the backend SentryBootstrap.ScrubPii exactly so the
// two surfaces cannot drift: we drop user email / IP / username but keep the
// opaque user id, null out cookies + query-string, and case-insensitively
// remove auth-ish headers. Anything we cannot scrub safely is dropped rather
// than shipped.
//
// Keep this file runtime-neutral: no Next.js imports, no Node-only APIs.

import type { Event, EventHint } from '@sentry/nextjs';

/**
 * Header names that must never be reported to Sentry. Matched
 * case-insensitively in scrubPii().
 */
export const SENSITIVE_HEADER_NAMES = [
  'Authorization',
  'Cookie',
  'Set-Cookie',
  'X-CSRF',
  'X-CSRF-Token',
  'X-XSRF-Token',
  'X-Api-Key',
  'X-Forwarded-For',
  'X-Real-IP',
] as const;

/**
 * Read the environment DSN from either the public or server-side env var.
 * Returns null when Sentry should stay disabled (tests, local dev, previews
 * that have not been explicitly opted in).
 */
export function readSentryDsn(): string | null {
  const dsn =
    (typeof process !== 'undefined' && process.env?.NEXT_PUBLIC_SENTRY_DSN) ||
    (typeof process !== 'undefined' && process.env?.SENTRY_DSN) ||
    '';
  return dsn.trim().length > 0 ? dsn.trim() : null;
}

/**
 * Read the Sentry environment (prod / staging / preview / dev). Defaults to
 * NODE_ENV so we never mix events across envs by accident.
 */
export function readSentryEnvironment(): string {
  if (typeof process === 'undefined') return 'production';
  return (
    process.env.SENTRY_ENVIRONMENT ||
    process.env.NEXT_PUBLIC_SENTRY_ENVIRONMENT ||
    process.env.NODE_ENV ||
    'production'
  );
}

/**
 * Read the release identifier. Falls back to an empty string which tells the
 * SDK to skip the release tag entirely rather than invent one.
 */
export function readSentryRelease(): string | undefined {
  if (typeof process === 'undefined') return undefined;
  const release =
    process.env.SENTRY_RELEASE ||
    process.env.NEXT_PUBLIC_SENTRY_RELEASE ||
    '';
  return release.trim().length > 0 ? release.trim() : undefined;
}

/**
 * Parse a 0..1 sample rate from an env var, clamping anything bogus to 0 so
 * enabling Sentry can never silently flip performance data on.
 */
export function readSampleRate(envVarName: string): number {
  if (typeof process === 'undefined') return 0;
  const raw = process.env[envVarName];
  if (!raw) return 0;
  const parsed = Number.parseFloat(raw);
  if (!Number.isFinite(parsed)) return 0;
  if (parsed < 0) return 0;
  if (parsed > 1) return 1;
  return parsed;
}

/**
 * beforeSend hook - strips PII before an event leaves the process. Exported
 * separately so unit tests can exercise the privacy contract without booting
 * the SDK.
 */
export function scrubPii<TEvent extends Event>(event: TEvent, _hint?: EventHint): TEvent | null {
  // User: keep the opaque id for grouping; drop everything that identifies a
  // human.
  if (event.user) {
    event.user = {
      id: event.user.id,
    };
  }

  // Request: drop cookies + query-string wholesale, case-insensitively remove
  // sensitive headers.
  if (event.request) {
    event.request.cookies = undefined;
    event.request.query_string = undefined;

    const headers = event.request.headers;
    if (headers && typeof headers === 'object') {
      const lookup = new Set(
        SENSITIVE_HEADER_NAMES.map((name) => name.toLowerCase()),
      );
      for (const key of Object.keys(headers)) {
        if (lookup.has(key.toLowerCase())) {
          delete (headers as Record<string, unknown>)[key];
        }
      }
    }
  }

  return event;
}
