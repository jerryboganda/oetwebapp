/**
 * In-memory view of the Owner Agent console unlock *state*.
 *
 * Security contract:
 * - The unlock credential itself is an HttpOnly cookie (`oet_owner_unlock`,
 *   1 hour, SameSite=Strict) set by POST /v1/owner-agent/unlock. JavaScript
 *   never sees it, and this module never holds, stores or forwards it.
 * - This store only keeps `{ unlocked, expiresAt }` so the UI knows whether to
 *   show the unlock screen. GET /me is the source of truth: a reload, a new tab
 *   or the console's CSP reload re-reads /me and comes back already unlocked
 *   while the cookie is valid.
 * - Nothing here is written to localStorage, sessionStorage, IndexedDB,
 *   document.cookie, the URL or any logger.
 * - Expiry is enforced on read AND by one timer that clears the state at
 *   `expiresAt` (the unlock lasts a fixed 60 minutes; there is no refresh).
 *
 * The store is a tiny external store so React reads it through
 * `useSyncExternalStore(subscribeUnlock, getUnlockSnapshot, getServerUnlockSnapshot)`.
 */

import type { OwnerAgentMe } from './types';

export type UnlockClearReason = 'locked' | 'expired' | 'server_locked';

export interface UnlockSnapshot {
  readonly unlocked: boolean;
  /** When the unlock ends (ISO-8601), or null when unknown / locked. */
  readonly expiresAt: string | null;
  /** Why the last unlock went away (null while unlocked or before any unlock). */
  readonly clearedReason: UnlockClearReason | null;
}

/** Expiry fields as they arrive from POST /unlock or GET /me. */
export interface UnlockExpiry {
  expiresAt?: string | null;
  absoluteExpiresAt?: string | null;
}

const EMPTY_SNAPSHOT: UnlockSnapshot = Object.freeze({
  unlocked: false,
  expiresAt: null,
  clearedReason: null,
});

const MAX_TIMER_MS = 2_147_483_000;

let snapshot: UnlockSnapshot = EMPTY_SNAPSHOT;
/** Bumped on every state change; lets async callers detect a concurrent unlock/lock. */
let generation = 0;
let expiryTimer: ReturnType<typeof setTimeout> | null = null;
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

function parseTime(value: string | null | undefined): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function cancelExpiryTimer(): void {
  if (expiryTimer !== null) {
    clearTimeout(expiryTimer);
    expiryTimer = null;
  }
}

function armExpiryTimer(now: number): void {
  cancelExpiryTimer();
  const expiresAt = parseTime(snapshot.expiresAt);
  if (!snapshot.unlocked || expiresAt === null) return;
  expiryTimer = setTimeout(() => {
    expiryTimer = null;
    if (isSnapshotUnlocked(snapshot)) {
      // Fired early because of the timer cap; wait for the rest.
      armExpiryTimer(Date.now());
      return;
    }
    clearUnlock('expired');
  }, Math.min(Math.max(expiresAt - now, 0), MAX_TIMER_MS));
}

/** The earlier of the two expiry fields (the server sets both to the same 60-minute mark). */
function effectiveExpiry(value: UnlockExpiry): string | null {
  const candidates = [value.expiresAt, value.absoluteExpiresAt]
    .filter((v): v is string => typeof v === 'string' && parseTime(v) !== null);
  if (candidates.length === 0) return null;
  return candidates.reduce((min, v) => ((parseTime(v) as number) < (parseTime(min) as number) ? v : min));
}

export function getUnlockSnapshot(): UnlockSnapshot {
  return snapshot;
}

/** Stable locked snapshot for server rendering. */
export function getServerUnlockSnapshot(): UnlockSnapshot {
  return EMPTY_SNAPSHOT;
}

/** True when the snapshot says unlocked and its expiry (if known) is still in the future. */
export function isSnapshotUnlocked(value: UnlockSnapshot, now: number = Date.now()): boolean {
  if (!value.unlocked) return false;
  const expiresAt = parseTime(value.expiresAt);
  return expiresAt === null || expiresAt > now;
}

/** True while the console is unlocked (and not past its expiry). */
export function isUnlocked(now: number = Date.now()): boolean {
  return isSnapshotUnlocked(snapshot, now);
}

/** Monotonic counter bumped on every unlock-state change. */
export function getUnlockGeneration(): number {
  return generation;
}

/**
 * Opaque identity of the current unlock, or null while locked. Changes when
 * the owner unlocks again, so callers can tell "the unlock I started with was
 * rejected" from "a newer unlock exists".
 */
export function getUnlockKey(now: number = Date.now()): string | null {
  return isSnapshotUnlocked(snapshot, now) ? `unlock-${generation}` : null;
}

/**
 * Mark the console unlocked until the given expiry (POST /unlock response or
 * GET /me). Any `ticket` field on the argument is ignored — it is never kept.
 * A no-op (no notification) when the state is unchanged.
 */
export function setUnlocked(value: UnlockExpiry, now: number = Date.now()): void {
  const expiresAt = effectiveExpiry(value ?? {});
  const expiresAtMs = parseTime(expiresAt);
  if (expiresAtMs !== null && expiresAtMs <= now) {
    clearUnlock('expired');
    return;
  }
  if (snapshot.unlocked && snapshot.expiresAt === expiresAt) return;
  snapshot = Object.freeze({ unlocked: true, expiresAt, clearedReason: null });
  generation += 1;
  armExpiryTimer(now);
  emit();
}

/** Drop the unlock state. Idempotent: clearing an already-locked store does not notify. */
export function clearUnlock(reason: UnlockClearReason = 'locked'): void {
  cancelExpiryTimer();
  if (!snapshot.unlocked) return;
  snapshot = Object.freeze({ ...EMPTY_SNAPSHOT, clearedReason: reason });
  generation += 1;
  emit();
}

/**
 * Apply a GET /me answer — the source of truth for the unlock state. Unlocked
 * with its expiry, or locked (a server-side lock/expiry/revocation clears a
 * previously unlocked state with `server_locked`).
 */
export function applyMeUnlock(me: OwnerAgentMe | null | undefined, now: number = Date.now()): void {
  if (me && me.isOwner === true && me.unlocked === true) {
    setUnlocked({ expiresAt: me.unlockExpiresAt ?? null, absoluteExpiresAt: me.absoluteExpiresAt ?? null }, now);
  } else {
    clearUnlock('server_locked');
  }
}

export function subscribeUnlock(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Test hook: restore the pristine state between specs. */
export function resetUnlockStoreForTests(): void {
  cancelExpiryTimer();
  snapshot = EMPTY_SNAPSHOT;
  generation = 0;
  listeners.clear();
}
