/**
 * In-memory holder for the Owner Agent unlock ticket.
 *
 * Security contract (plan Phase 3 "page memory only"):
 * - The ticket lives in this module's closure and nowhere else. It is NEVER
 *   written to localStorage, sessionStorage, IndexedDB, cookies, the URL or any
 *   logger. A reload, a new tab or leaving the console drops it, and the owner
 *   unlocks again with password + TOTP.
 * - Expiry is enforced on read: an expired ticket is reported as absent even
 *   before the refresh scheduler (lib/owner-agent/api.ts) clears it.
 *
 * The store is a tiny external store so React reads it through
 * `useSyncExternalStore(subscribeUnlock, getUnlockSnapshot, getServerUnlockSnapshot)`.
 */

import type { UnlockResponse } from './types';

export type UnlockClearReason = 'locked' | 'expired' | 'server_locked' | 'refresh_failed';

export interface UnlockSnapshot {
  readonly ticket: string | null;
  readonly expiresAt: string | null;
  readonly absoluteExpiresAt: string | null;
  /** Why the last ticket went away (null while unlocked or before any unlock). */
  readonly clearedReason: UnlockClearReason | null;
}

const EMPTY_SNAPSHOT: UnlockSnapshot = Object.freeze({
  ticket: null,
  expiresAt: null,
  absoluteExpiresAt: null,
  clearedReason: null,
});

let snapshot: UnlockSnapshot = EMPTY_SNAPSHOT;
const listeners = new Set<() => void>();

function emit(): void {
  for (const listener of Array.from(listeners)) {
    try {
      listener();
    } catch {
      // A broken subscriber must never prevent the others from seeing a lock.
    }
  }
}

function parseTime(value: string | null): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function getUnlockSnapshot(): UnlockSnapshot {
  return snapshot;
}

/** Stable empty snapshot for server rendering (the ticket never exists on the server). */
export function getServerUnlockSnapshot(): UnlockSnapshot {
  return EMPTY_SNAPSHOT;
}

/** True when the snapshot carries a ticket whose sliding and absolute expiry are both in the future. */
export function isSnapshotUnlocked(value: UnlockSnapshot, now: number = Date.now()): boolean {
  if (!value.ticket) return false;
  const expiresAt = parseTime(value.expiresAt);
  if (expiresAt !== null && expiresAt <= now) return false;
  const absolute = parseTime(value.absoluteExpiresAt);
  if (absolute !== null && absolute <= now) return false;
  return true;
}

/** The ticket to send as `X-Owner-Agent-Unlock`, or null when locked/expired. */
export function getUnlockTicket(now: number = Date.now()): string | null {
  return isSnapshotUnlocked(snapshot, now) ? snapshot.ticket : null;
}

/** Store a freshly minted ticket (POST /unlock or /unlock/refresh). */
export function setUnlock(response: UnlockResponse): void {
  if (!response || typeof response.ticket !== 'string' || response.ticket.length === 0) {
    throw new Error('Unlock response did not include a ticket.');
  }
  snapshot = Object.freeze({
    ticket: response.ticket,
    expiresAt: typeof response.expiresAt === 'string' ? response.expiresAt : null,
    absoluteExpiresAt: typeof response.absoluteExpiresAt === 'string' ? response.absoluteExpiresAt : null,
    clearedReason: null,
  });
  emit();
}

/** Drop the ticket. Idempotent: clearing an already-empty store does not notify. */
export function clearUnlock(reason: UnlockClearReason = 'locked'): void {
  if (snapshot.ticket === null) return;
  snapshot = Object.freeze({ ...EMPTY_SNAPSHOT, clearedReason: reason });
  emit();
}

export function subscribeUnlock(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Test hook: restore the pristine state between specs. */
export function resetUnlockStoreForTests(): void {
  snapshot = EMPTY_SNAPSHOT;
  listeners.clear();
}
