/**
 * Pure building blocks of the Writing draft sync engine
 * (`hooks/use-writing-draft-sync.ts`): the per-device shadow copy, retry
 * backoff and the server-vs-device reconcile rules. No React, no network.
 */
import type { WritingDraftPhase, WritingDraftV2Dto, WritingEditorMode } from './types';

export const DRAFT_SHADOW_PREFIX = 'oet:writing-draft:v1:';
const SHADOW_MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000;
/** Waits after consecutive failed saves; the last value repeats. */
export const DRAFT_RETRY_DELAYS_MS = [2_000, 4_000, 8_000, 16_000, 30_000];
/** Browsers cap keepalive bodies at 64 KiB per page; leave headroom. */
export const KEEPALIVE_MAX_BYTES = 60_000;

/** The exam clock as saved: seconds LEFT in each window (pause-while-away). */
export interface DraftTimers {
  phase: WritingDraftPhase | null;
  readingSecondsRemaining: number | null;
  writingSecondsRemaining: number | null;
}

/** What this device knows about the draft, written synchronously on every change. */
export interface DraftShadow extends DraftTimers {
  /** Letter text the server has not confirmed yet. Absent once synced (no plaintext at rest). */
  text?: string;
  wordCount?: number;
  /** Text of a save that was sent but never confirmed (its response may have been lost). */
  sentText?: string;
  /** Server version the device text was typed on top of (0 = no row yet, null = unknown). */
  baseVersion: number | null;
  savedAt: number;
}

export function draftShadowKey(userId: string, scenarioId: string, mode: WritingEditorMode): string {
  return `${DRAFT_SHADOW_PREFIX}${userId}:${scenarioId}:${mode}`;
}

function storage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

const isOptional = (value: unknown, type: 'string' | 'number') => value === undefined || typeof value === type;
const isSecondsOrNull = (value: unknown) =>
  value === undefined || value === null || (typeof value === 'number' && Number.isFinite(value));

/** The stored value has exactly the shape this module writes (anything else is treated as no copy). */
function isDraftShadow(value: unknown): value is DraftShadow {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.savedAt === 'number' && Number.isFinite(v.savedAt)
    && isOptional(v.text, 'string')
    && isOptional(v.sentText, 'string')
    && isOptional(v.wordCount, 'number')
    && (v.baseVersion === null || (typeof v.baseVersion === 'number' && Number.isInteger(v.baseVersion)))
    && (v.phase === undefined || v.phase === null || v.phase === 'reading' || v.phase === 'writing')
    && isSecondsOrNull(v.readingSecondsRemaining)
    && isSecondsOrNull(v.writingSecondsRemaining)
  );
}

export function readDraftShadow(key: string): DraftShadow | null {
  try {
    const raw = storage()?.getItem(key);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    return isDraftShadow(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export function writeDraftShadow(key: string, shadow: DraftShadow): void {
  try {
    storage()?.setItem(key, JSON.stringify(shadow));
  } catch {
    // Quota or a blocked storage: the server save still runs.
  }
}

export function clearDraftShadow(key: string): void {
  try {
    storage()?.removeItem(key);
  } catch {
    // ignore
  }
}

/** Drops device copies untouched for 30 days (and unreadable ones). */
export function sweepDraftShadows(now = Date.now()): void {
  const store = storage();
  if (!store) return;
  try {
    const stale: string[] = [];
    for (let i = 0; i < store.length; i += 1) {
      const key = store.key(i);
      if (!key?.startsWith(DRAFT_SHADOW_PREFIX)) continue;
      const shadow = readDraftShadow(key);
      if (!shadow || now - shadow.savedAt > SHADOW_MAX_AGE_MS) stale.push(key);
    }
    stale.forEach((key) => store.removeItem(key));
  } catch {
    // ignore
  }
}

export function retryDelayMs(failures: number): number {
  return DRAFT_RETRY_DELAYS_MS[Math.min(Math.max(failures, 1), DRAFT_RETRY_DELAYS_MS.length) - 1];
}

function isSeconds(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

/** The smaller remaining time: a clock never gains time by being restored. */
export function minSeconds(a: number | null | undefined, b: number | null | undefined): number | null {
  if (isSeconds(a) && isSeconds(b)) return Math.min(a, b);
  return isSeconds(a) ? a : isSeconds(b) ? b : null;
}

/** The phase never moves backwards (reading → writing). */
export function laterPhase(
  a: WritingDraftPhase | null | undefined,
  b: WritingDraftPhase | null | undefined,
): WritingDraftPhase | null {
  if (a === 'writing' || b === 'writing') return 'writing';
  return a ?? b ?? null;
}

export interface ReconciledDraft extends DraftTimers {
  /** Text the editor starts with. */
  text: string;
  wordCount: number;
  /** Text the server holds for this attempt (null = no row yet). */
  serverText: string | null;
  /** `expectedVersion` for the next save: 0 = create, null = unknown (legacy write). */
  version: number | null;
  /** The device holds unsynced text but the server moved on since: the learner chooses. */
  conflict: boolean;
  /** Where the editor text came from. */
  source: 'server' | 'device' | 'none';
}

/**
 * Server row (active, or null for a 404) vs this device's shadow:
 * - no unsynced device text → the server copy;
 * - device text equal to the server copy → synced;
 * - device text typed on top of the server's CURRENT version (or of a save whose
 *   response was lost) → the device copy wins and is saved next;
 * - otherwise another tab/device saved since → keep the device text in the
 *   editor and flag a conflict.
 * Timers merge (min, later phase) only when the shadow belongs to the server's
 * current version; a stale shadow never shortens a newer attempt's clock.
 */
export function reconcileDraft(server: WritingDraftV2Dto | null, shadow: DraftShadow | null): ReconciledDraft {
  const serverVersion = server ? (server.version ?? null) : 0;
  const shadowApplies = !!shadow && (
    !server
    || serverVersion === null
    || shadow.baseVersion === serverVersion
    || (shadow.sentText !== undefined && shadow.sentText === server.content)
  );
  const timers: DraftTimers = shadowApplies && shadow
    ? {
        phase: laterPhase(server?.phase, shadow.phase),
        readingSecondsRemaining: minSeconds(server?.readingSecondsRemaining, shadow.readingSecondsRemaining),
        writingSecondsRemaining: minSeconds(server?.writingSecondsRemaining, shadow.writingSecondsRemaining),
      }
    : {
        phase: server?.phase ?? null,
        readingSecondsRemaining: server?.readingSecondsRemaining ?? null,
        writingSecondsRemaining: server?.writingSecondsRemaining ?? null,
      };
  const base = {
    ...timers,
    serverText: server ? server.content : null,
    version: serverVersion,
  };
  const local = shadow?.text;
  if (local === undefined || (server && local === server.content)) {
    return {
      ...base,
      text: server?.content ?? '',
      wordCount: server?.wordCount ?? 0,
      conflict: false,
      source: server ? 'server' : 'none',
    };
  }
  return {
    ...base,
    text: local,
    wordCount: shadow?.wordCount ?? 0,
    conflict: !shadowApplies,
    source: 'device',
  };
}
