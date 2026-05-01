'use client';

/**
 * Mocks V2 Wave 2 — proctoring telemetry hook.
 *
 * Listens for browser-level integrity signals during a mock attempt and ships
 * them to the backend in batched POSTs. Designed to be cheap and fail-safe:
 *  - Events are coalesced in an in-memory queue and flushed on a timer
 *    (default 8s) plus on `pagehide`/`beforeunload` for best-effort delivery.
 *  - Network failures are swallowed (telemetry is non-critical).
 *  - Caller decides which kinds to listen for via the `enabled` flag.
 *
 * Note: must always be invoked unconditionally (no early-return before hooks)
 * — when `enabled === false`, internal listeners simply no-op.
 */

import { useCallback, useEffect, useRef } from 'react';

import {
  MOCK_PROCTORING_KINDS,
  type MockProctoringEventInput,
  type MockProctoringKind,
  type MockProctoringSeverity,
  recordMockProctoringEvents,
} from '@/lib/api';

interface UseMockProctoringOptions {
  /** Mock attempt id to associate events with. */
  attemptId: string | null | undefined;
  /** Currently-active section attempt id (optional). */
  sectionAttemptId?: string | null;
  /** Master switch — pass false during practice / unstrict mocks. */
  enabled: boolean;
  /** Listeners to install. Defaults to the full proctoring set. */
  listenFor?: ReadonlySet<MockProctoringKind>;
  /** Coalesce/flush interval in ms. */
  flushIntervalMs?: number;
  /** Prevent paste in strict exam-like mocks while recording the advisory event. */
  blockPaste?: boolean;
}

const DEFAULT_LISTEN_FOR: ReadonlySet<MockProctoringKind> = new Set(MOCK_PROCTORING_KINDS);

export interface UseMockProctoringApi {
  /** Manually report an event (e.g. from a "Report audio issue" button). */
  report: (kind: MockProctoringKind, opts?: { severity?: MockProctoringSeverity; metadata?: Record<string, unknown> }) => void;
  /** Force-flush the queue (e.g. on section completion). */
  flush: () => Promise<void>;
}

export function useMockProctoring(options: UseMockProctoringOptions): UseMockProctoringApi {
  const {
    attemptId,
    sectionAttemptId = null,
    enabled,
    listenFor = DEFAULT_LISTEN_FOR,
    flushIntervalMs = 8000,
    blockPaste = false,
  } = options;

  const queueRef = useRef<MockProctoringEventInput[]>([]);
  const attemptIdRef = useRef<string | null | undefined>(attemptId);
  const sectionIdRef = useRef<string | null>(sectionAttemptId);
  const enabledRef = useRef<boolean>(enabled);
  const inFlightRef = useRef<boolean>(false);

  attemptIdRef.current = attemptId;
  sectionIdRef.current = sectionAttemptId;
  enabledRef.current = enabled;

  const flush = useCallback(async () => {
    const id = attemptIdRef.current;
    if (!id || queueRef.current.length === 0 || inFlightRef.current) return;
    const batch = queueRef.current.splice(0, 50);
    inFlightRef.current = true;
    try {
      await recordMockProctoringEvents(id, batch);
    } catch {
      // Telemetry must never break the player. Drop on failure.
    } finally {
      inFlightRef.current = false;
    }
  }, []);

  const enqueue = useCallback(
    (kind: MockProctoringKind, opts?: { severity?: MockProctoringSeverity; metadata?: Record<string, unknown> }) => {
      if (!enabledRef.current || !attemptIdRef.current) return;
      queueRef.current.push({
        kind,
        occurredAt: new Date().toISOString(),
        mockSectionAttemptId: sectionIdRef.current ?? undefined,
        severity: opts?.severity,
        metadata: opts?.metadata,
      });
      // Flush eagerly when queue grows past half-capacity.
      if (queueRef.current.length >= 25) {
        void flush();
      }
    },
    [flush],
  );

  // Periodic flush.
  useEffect(() => {
    if (!enabled || !attemptId) return;
    const id = window.setInterval(() => { void flush(); }, flushIntervalMs);
    return () => window.clearInterval(id);
  }, [enabled, attemptId, flushIntervalMs, flush]);

  // Browser integrity listeners.
  useEffect(() => {
    if (!enabled || typeof document === 'undefined') return;

    const handlers: Array<() => void> = [];

    if (listenFor.has('visibility_hidden')) {
      const onVisibility = () => {
        if (document.visibilityState === 'hidden') {
          enqueue('visibility_hidden', { severity: 'warning' });
        }
      };
      document.addEventListener('visibilitychange', onVisibility);
      handlers.push(() => document.removeEventListener('visibilitychange', onVisibility));
    }

    if (listenFor.has('tab_switch')) {
      const onBlur = () => enqueue('tab_switch', { severity: 'warning' });
      window.addEventListener('blur', onBlur);
      handlers.push(() => window.removeEventListener('blur', onBlur));
    }

    if (listenFor.has('fullscreen_exit')) {
      const onFsChange = () => {
        if (!document.fullscreenElement) {
          enqueue('fullscreen_exit', { severity: 'warning' });
        }
      };
      document.addEventListener('fullscreenchange', onFsChange);
      handlers.push(() => document.removeEventListener('fullscreenchange', onFsChange));
    }

    if (listenFor.has('paste_blocked')) {
      const onPaste = (event: ClipboardEvent) => {
        if (blockPaste) event.preventDefault();
        enqueue('paste_blocked', { severity: 'info', metadata: { hasClipboardData: !!event.clipboardData } });
      };
      document.addEventListener('paste', onPaste);
      handlers.push(() => document.removeEventListener('paste', onPaste));
    }

    if (listenFor.has('network_drop')) {
      const onOffline = () => enqueue('network_drop', { severity: 'warning' });
      window.addEventListener('offline', onOffline);
      handlers.push(() => window.removeEventListener('offline', onOffline));
    }

    const onPageHide = () => { void flush(); };
    window.addEventListener('pagehide', onPageHide);
    handlers.push(() => window.removeEventListener('pagehide', onPageHide));

    return () => {
      for (const off of handlers) off();
      void flush();
    };
  }, [blockPaste, enabled, listenFor, enqueue, flush]);

  return { report: enqueue, flush };
}
