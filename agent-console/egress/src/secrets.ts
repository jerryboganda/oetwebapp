// Kept byte-identical in egress/src/ and dockerproxy/src/ (drift check:
// agent-console/tests/proxy-shared-files.test.ts). Edit both copies.
import { createHash, timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';

/** Shared proxy token minimum length (CONTRACT.md §6: ≥ 32 chars). */
export const MIN_TOKEN_LENGTH = 32;

/**
 * Reads the shared proxy token (compose secret `owner_agent_proxy_token`).
 * Throws — so the process refuses to start — when the file is missing,
 * unreadable, too short or contains whitespace. The token value is never
 * included in error messages.
 */
export function readTokenFile(path: string, read: (p: string) => string = (p) => readFileSync(p, 'utf8')): string {
  let raw: string;
  try {
    raw = read(path);
  } catch (err) {
    const code = (err as NodeJS.ErrnoException).code ?? 'EUNKNOWN';
    throw new Error(`proxy token file ${path} is not readable (${code})`);
  }
  const token = raw.trim();
  if (token.length < MIN_TOKEN_LENGTH) {
    throw new Error(`proxy token in ${path} must be at least ${MIN_TOKEN_LENGTH} characters`);
  }
  if (/\s/.test(token)) {
    throw new Error(`proxy token in ${path} must not contain whitespace`);
  }
  return token;
}

/** Constant-time string comparison (hashes first so lengths never leak). */
export function constantTimeEqual(a: string, b: string): boolean {
  const da = createHash('sha256').update(a, 'utf8').digest();
  const db = createHash('sha256').update(b, 'utf8').digest();
  return timingSafeEqual(da, db);
}
