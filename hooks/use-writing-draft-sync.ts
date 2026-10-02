'use client';

import { useEffect, useRef, useState } from 'react';
import { isApiError } from '@/lib/api/client';
import { getWritingDraftV2, putWritingDraftV2 } from '@/lib/writing/api';
import { countLetterWords } from '@/lib/writing/letter-text';
import {
  clearDraftShadow,
  draftShadowKey,
  KEEPALIVE_MAX_BYTES,
  retryDelayMs,
  sweepDraftShadows,
  writeDraftShadow,
  type DraftTimers,
} from '@/lib/writing/draft-sync';
import type { WritingDraftV2UpsertPayload, WritingEditorMode } from '@/lib/writing/types';

/** `data-state` contract of `writing-draft-status`. */
export type DraftSyncState = 'saved' | 'saving' | 'pending-local' | 'offline' | 'error';

/** What the editor starts with, once the page has reconciled server and device copies. */
export interface DraftSyncBaseline {
  text: string;
  wordCount: number;
  /** Text the server holds for this attempt (null = no row yet: the first save creates it). */
  serverText: string | null;
  /** `expectedVersion` for the next save: 0 = create, null = unknown (legacy unconditional write). */
  version: number | null;
  /** The device text conflicts with a newer server copy; nothing is saved until the learner chooses. */
  conflict?: boolean;
}

/** The exam clock at the moment of a save (pause-while-away seconds). */
export interface DraftClockSnapshot extends DraftTimers {
  timeSpentSeconds: number;
}

export interface UseWritingDraftSyncOptions {
  scenarioId: string;
  mode: WritingEditorMode;
  /** Scopes the device copy to the signed-in account. */
  userId: string;
  /** null until the page knows what the editor starts with — nothing is saved before that. */
  baseline: DraftSyncBaseline | null;
  /** Read at every save; omitted on pages without an exam clock. */
  getClock?: () => DraftClockSnapshot;
  /** Saves at this cadence even without edits so the server keeps the remaining time. */
  heartbeatMs?: number | null;
}

export interface WritingDraftSync {
  state: DraftSyncState;
  online: boolean;
  conflict: { serverText: string } | null;
  /** Every editor change: device copy now, server save after 1.2 s quiet (5 s at most). */
  update(text: string, wordCount: number): void;
  /** Save now (blur, phase change). `keepalive` lets it outlive the page. */
  flush(keepalive?: boolean): void;
  /** Conflict: overwrite the other version with this device's text. */
  keepLocal(): void;
  /** Conflict: adopt the other version; returns the text the editor must show. */
  takeServer(): string;
  hasUnsynced(): boolean;
  /** After a successful submit: stop syncing and drop the device copy. */
  discard(): void;
}

const DEBOUNCE_MS = 1_200;
const MAX_WAIT_MS = 5_000;

interface EngineDeps {
  key: string;
  scenarioId: string;
  mode: WritingEditorMode;
  getClock?: () => DraftClockSnapshot;
  setState: (state: DraftSyncState) => void;
  setConflict: (conflict: { serverText: string } | null) => void;
}

function isRetryable(err: unknown): boolean {
  if (!isApiError(err)) return true;
  return err.status === 0 || err.status === 408 || err.status === 429 || err.status >= 500;
}

/**
 * Single-flight save loop. Everything lives in one mutable record so the
 * timers, browser events and in-flight promises always see the latest values.
 */
function createEngine(deps: { current: EngineDeps }) {
  type Timer = ReturnType<typeof setTimeout> | undefined;
  const s = {
    ready: false,
    stopped: false,
    unmounted: false,
    text: '',
    words: 0,
    serverText: null as string | null,
    version: null as number | null,
    sentText: undefined as string | undefined,
    inFlight: false,
    again: false,
    failures: 0,
    rejected: false,
    conflicted: false,
    conflictText: '',
    conflictVersion: null as number | null,
    debounce: undefined as Timer,
    retry: undefined as Timer,
    firstDirtyAt: null as number | null,
  };
  const isOnline = () => typeof navigator === 'undefined' || navigator.onLine !== false;
  const dirty = () => s.text !== s.serverText;
  const halted = () => !s.ready || s.stopped || s.unmounted;

  const refresh = () => {
    let next: DraftSyncState;
    if (s.conflicted || s.rejected) next = 'error';
    else if (!isOnline()) next = dirty() ? 'pending-local' : 'offline';
    else if (s.failures > 0) next = 'pending-local';
    else next = dirty() ? 'saving' : 'saved';
    deps.current.setState(next);
  };

  // Synchronous device copy: plaintext only while the server lacks it.
  const persist = () => {
    if (!s.ready || s.stopped) return;
    const clock = deps.current.getClock?.();
    const unsynced = dirty();
    writeDraftShadow(deps.current.key, {
      ...(unsynced ? { text: s.text, wordCount: s.words } : {}),
      ...(unsynced && s.sentText !== undefined ? { sentText: s.sentText } : {}),
      baseVersion: s.version,
      phase: clock?.phase ?? null,
      readingSecondsRemaining: clock?.readingSecondsRemaining ?? null,
      writingSecondsRemaining: clock?.writingSecondsRemaining ?? null,
      savedAt: Date.now(),
    });
  };

  const scheduleRetry = () => {
    clearTimeout(s.retry);
    if (halted()) return;
    s.retry = setTimeout(() => {
      s.retry = undefined;
      save();
    }, retryDelayMs(s.failures));
  };

  const onSaved = (text: string, version: number | undefined) => {
    s.version = typeof version === 'number' ? version : null;
    s.serverText = text;
    s.sentText = undefined;
    s.failures = 0;
    s.rejected = false;
    if (s.text !== text) s.again = true;
    persist();
  };

  // 409 = someone else saved since our version: same text means it was us.
  const onConflict = async (text: string) => {
    const { scenarioId, mode } = deps.current;
    const server = await getWritingDraftV2(scenarioId, mode);
    if (!server) {
      s.version = 0;
      s.again = true;
      return;
    }
    const serverVersion = server.version ?? null;
    if (server.content === text || server.content === s.text) {
      s.version = serverVersion;
      s.serverText = server.content;
      s.sentText = undefined;
      s.failures = 0;
      s.rejected = false;
      if (s.text !== server.content) s.again = true;
      persist();
      return;
    }
    s.conflicted = true;
    s.conflictText = server.content;
    s.conflictVersion = serverVersion;
    deps.current.setConflict({ serverText: server.content });
    persist();
  };

  const onFailed = async (text: string, err: unknown) => {
    let failure = err;
    if (isApiError(err) && err.status === 409) {
      try {
        await onConflict(text);
        return;
      } catch (getErr) {
        failure = getErr;
      }
    }
    s.failures += 1;
    s.rejected = !isRetryable(failure);
    scheduleRetry();
  };

  function save(keepalive = false) {
    clearTimeout(s.debounce);
    s.debounce = undefined;
    s.firstDirtyAt = null;
    if (!s.ready || s.stopped || s.conflicted) return;
    if (s.inFlight) {
      s.again = true;
      return;
    }
    if (!isOnline()) {
      refresh();
      return;
    }
    clearTimeout(s.retry);
    s.retry = undefined;
    const text = s.text;
    const clock = deps.current.getClock?.();
    const payload: WritingDraftV2UpsertPayload = {
      content: text,
      wordCount: s.words,
      timeSpentSeconds: clock?.timeSpentSeconds ?? 0,
      ...(s.version === null ? {} : { expectedVersion: s.version }),
      ...(clock
        ? {
            phase: clock.phase,
            readingSecondsRemaining: clock.readingSecondsRemaining,
            writingSecondsRemaining: clock.writingSecondsRemaining,
          }
        : {}),
    };
    const init = keepalive && JSON.stringify(payload).length < KEEPALIVE_MAX_BYTES ? { keepalive: true } : undefined;
    s.inFlight = true;
    if (dirty()) s.sentText = text;
    persist();
    refresh();
    const { scenarioId, mode } = deps.current;
    void putWritingDraftV2(scenarioId, mode, payload, init)
      .then((dto) => onSaved(text, dto?.version))
      .catch((err: unknown) => onFailed(text, err))
      .finally(() => {
        s.inFlight = false;
        if (s.again && !halted()) {
          s.again = false;
          save();
        }
        refresh();
      });
  }

  return {
    begin(baseline: DraftSyncBaseline) {
      s.ready = true;
      s.stopped = false;
      s.text = baseline.text;
      s.words = baseline.wordCount;
      s.serverText = baseline.serverText;
      s.version = baseline.version;
      s.conflicted = Boolean(baseline.conflict) && baseline.serverText !== null;
      s.conflictText = baseline.serverText ?? '';
      s.conflictVersion = baseline.version;
      deps.current.setConflict(s.conflicted ? { serverText: s.conflictText } : null);
      persist();
      if (dirty() && !s.conflicted) save();
      else refresh();
    },
    update(text: string, wordCount: number) {
      s.text = text;
      s.words = wordCount;
      if (halted()) return;
      persist();
      // While a retry is scheduled it carries the latest text; typing must not hammer a failing server.
      if (!s.conflicted && !s.retry) {
        const now = Date.now();
        if (s.firstDirtyAt === null) s.firstDirtyAt = now;
        clearTimeout(s.debounce);
        s.debounce = setTimeout(() => save(), Math.max(0, Math.min(DEBOUNCE_MS, s.firstDirtyAt + MAX_WAIT_MS - now)));
      }
      refresh();
    },
    flush(keepalive = false) {
      persist();
      save(keepalive);
    },
    heartbeat() {
      persist();
      if (!s.inFlight && !s.retry) save();
    },
    online() {
      if (dirty() || s.failures > 0) save();
      refresh();
    },
    refresh,
    keepLocal() {
      if (!s.conflicted) return;
      s.conflicted = false;
      s.version = s.conflictVersion;
      deps.current.setConflict(null);
      save();
    },
    takeServer() {
      if (!s.conflicted) return s.text;
      s.conflicted = false;
      s.version = s.conflictVersion;
      s.text = s.conflictText;
      s.serverText = s.conflictText;
      s.words = countLetterWords(s.conflictText);
      s.sentText = undefined;
      deps.current.setConflict(null);
      persist();
      refresh();
      return s.text;
    },
    hasUnsynced() {
      return !halted() && (dirty() || s.inFlight);
    },
    discard() {
      s.stopped = true;
      clearTimeout(s.debounce);
      clearTimeout(s.retry);
      clearDraftShadow(deps.current.key);
      deps.current.setState('saved');
    },
    mount() {
      s.unmounted = false;
    },
    unmount() {
      if (s.ready && !s.stopped) {
        persist();
        save(true);
      }
      s.unmounted = true;
      clearTimeout(s.debounce);
      clearTimeout(s.retry);
    },
  };
}

/**
 * Zero-loss autosave for a Writing draft: a synchronous device copy on every
 * change, a debounced single-flight compare-and-set save, a heartbeat that
 * keeps the server's remaining time fresh, keepalive flushes when the page is
 * hidden or closed, backoff (2/4/8/16/30 s) plus an immediate retry when the
 * connection returns, and 409 conflict resolution.
 */
export function useWritingDraftSync({
  scenarioId,
  mode,
  userId,
  baseline,
  getClock,
  heartbeatMs = null,
}: UseWritingDraftSyncOptions): WritingDraftSync {
  const [state, setState] = useState<DraftSyncState>('saved');
  const [conflict, setConflict] = useState<{ serverText: string } | null>(null);
  const [online, setOnline] = useState(() => typeof navigator === 'undefined' || navigator.onLine !== false);
  const deps = useRef<EngineDeps>({ key: '', scenarioId, mode, getClock, setState, setConflict });
  deps.current = { key: draftShadowKey(userId, scenarioId, mode), scenarioId, mode, getClock, setState, setConflict };
  const [engine] = useState(() => createEngine(deps));

  useEffect(() => {
    engine.mount();
    sweepDraftShadows();
    const onOnline = () => {
      setOnline(true);
      engine.online();
    };
    const onOffline = () => {
      setOnline(false);
      engine.refresh();
    };
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') engine.flush(true);
    };
    const onPageHide = () => engine.flush(true);
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!engine.hasUnsynced()) return;
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    document.addEventListener('visibilitychange', onVisibility);
    window.addEventListener('pagehide', onPageHide);
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => {
      window.removeEventListener('online', onOnline);
      window.removeEventListener('offline', onOffline);
      document.removeEventListener('visibilitychange', onVisibility);
      window.removeEventListener('pagehide', onPageHide);
      window.removeEventListener('beforeunload', onBeforeUnload);
      engine.unmount();
    };
  }, [engine]);

  useEffect(() => {
    if (baseline) engine.begin(baseline);
  }, [engine, baseline]);

  useEffect(() => {
    if (!heartbeatMs || !baseline) return;
    const id = setInterval(() => engine.heartbeat(), heartbeatMs);
    return () => clearInterval(id);
  }, [engine, heartbeatMs, baseline]);

  return {
    state,
    online,
    conflict,
    update: engine.update,
    flush: engine.flush,
    keepLocal: engine.keepLocal,
    takeServer: engine.takeServer,
    hasUnsynced: engine.hasUnsynced,
    discard: engine.discard,
  };
}
