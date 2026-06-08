'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Per-question highlight / strikethrough payload persisted to the backend.
 * The shape is opaque to the server (capped at 64 KB) — feel free to add
 * fields on the frontend as long as they round-trip through JSON.stringify
 * cleanly. Shared by the Listening and Reading exam runners: the persistence
 * contract is identical across subtests, only the API endpoint differs.
 */
export interface AttemptQuestionAnnotation {
  /** Set of opaque highlight ranges or labels. Frontend defines the schema. */
  highlights?: string[];
  /** Option keys (e.g. "A", "B", "C") the learner has crossed out. */
  struckOptions?: string[];
  /** True when the question stem itself is highlighted. */
  stemHighlighted?: boolean;
  /**
   * True when the learner has flagged this question for review.
   * Persisted opaquely in the annotations blob alongside highlights/strike —
   * no backend field or migration needed. Used by Part B/C MCQ renderers.
   */
  flagged?: boolean;
}

export interface AttemptAnnotationsState {
  /** Question id → per-question annotation payload. */
  byQuestion: Record<string, AttemptQuestionAnnotation>;
}

/**
 * Minimal persistence surface a runner must provide. Satisfied structurally
 * by `listeningV2Api` and `readingAnnotationsApi` (both expose these two
 * methods, among others).
 */
export interface AttemptAnnotationsApi {
  saveAnnotations(attemptId: string, annotationsJson: string | null): Promise<unknown>;
  getAnnotations(attemptId: string): Promise<{ annotationsJson: string | null }>;
}

const EMPTY_STATE: AttemptAnnotationsState = { byQuestion: {} };
const DEBOUNCE_MS = 400;
const MAX_BYTES = 64 * 1024;

function safeParse(raw: string | null | undefined): AttemptAnnotationsState {
  if (!raw) return EMPTY_STATE;
  try {
    const parsed = JSON.parse(raw) as Partial<AttemptAnnotationsState>;
    if (!parsed || typeof parsed !== 'object' || !parsed.byQuestion) return EMPTY_STATE;
    return { byQuestion: parsed.byQuestion as Record<string, AttemptQuestionAnnotation> };
  } catch {
    return EMPTY_STATE;
  }
}

/**
 * `useAttemptAnnotations` — debounced (400 ms) auto-save of highlight +
 * strikethrough state for an exam attempt. This is the shared core behind
 * `useListeningAnnotations` and `useReadingAnnotations`; the only difference
 * between subtests is the injected `api` (which endpoint the payload PUTs to).
 *
 * Hydration: pass the initial payload (from the attempt / session DTO) on
 * mount; the hook stores a copy and only re-parses if `attemptId` /
 * `initialAnnotationsJson` change. Callers can also invoke `reload()` to
 * re-pull from the server.
 *
 * The save call uses last-write-wins; if the user keeps editing during a
 * pending save, the hook coalesces edits and fires one final PUT.
 */
export function useAttemptAnnotations(options: {
  attemptId: string | null;
  api: AttemptAnnotationsApi;
  initialAnnotationsJson?: string | null;
  disabled?: boolean;
}) {
  const { attemptId, api, initialAnnotationsJson, disabled } = options;
  // Keep the latest `api` in a ref so callers may pass an inline object
  // without thrashing the debounce effect. flush()/reload() therefore depend
  // only on (attemptId, disabled, state) — identical to the pre-extraction
  // Listening hook, so its debounce test holds byte-for-byte. (In practice both
  // callers pass a stable module singleton, so this effect is a no-op after
  // mount; the ref is initialised with `api` so it's correct on first render.)
  const apiRef = useRef(api);
  useEffect(() => {
    apiRef.current = api;
  }, [api]);

  const [state, setState] = useState<AttemptAnnotationsState>(() => safeParse(initialAnnotationsJson));
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [lastSavedAt, setLastSavedAt] = useState<Date | null>(null);
  const [tooLarge, setTooLarge] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const dirtyRef = useRef(false);
  const lastPayloadRef = useRef<string | null>(null);

  // Hydrate when attemptId changes.
  useEffect(() => {
    setState(safeParse(initialAnnotationsJson));
    dirtyRef.current = false;
    setSaveStatus('idle');
    setTooLarge(false);
    lastPayloadRef.current = initialAnnotationsJson ?? null;
  }, [attemptId, initialAnnotationsJson]);

  const flush = useCallback(async () => {
    if (!attemptId || disabled) return;
    const payload = JSON.stringify(state);
    if (payload === lastPayloadRef.current) {
      setSaveStatus('saved');
      return;
    }
    // Local size guard — avoid wasting a roundtrip when we already know
    // the server will reject. The backend enforces the same 64 KB cap.
    const bytes = new Blob([payload]).size;
    if (bytes > MAX_BYTES) {
      setTooLarge(true);
      setSaveStatus('error');
      return;
    }
    setTooLarge(false);
    setSaveStatus('saving');
    try {
      // Empty annotations → null payload (server treats blank as clear).
      const wirePayload = state.byQuestion && Object.keys(state.byQuestion).length === 0 ? null : payload;
      await apiRef.current.saveAnnotations(attemptId, wirePayload);
      lastPayloadRef.current = payload;
      dirtyRef.current = false;
      setSaveStatus('saved');
      setLastSavedAt(new Date());
    } catch {
      setSaveStatus('error');
    }
  }, [attemptId, disabled, state]);

  // Debounced auto-save when state changes.
  useEffect(() => {
    if (!attemptId || disabled || !dirtyRef.current) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      void flush();
    }, DEBOUNCE_MS);
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [attemptId, disabled, flush]);

  const update = useCallback((questionId: string, mutator: (current: AttemptQuestionAnnotation) => AttemptQuestionAnnotation) => {
    setState((prev) => {
      const current = prev.byQuestion[questionId] ?? {};
      const next = mutator(current);
      const trimmedByQuestion = { ...prev.byQuestion };
      const isEmpty = !next.stemHighlighted
        && !next.flagged
        && (!next.highlights || next.highlights.length === 0)
        && (!next.struckOptions || next.struckOptions.length === 0);
      if (isEmpty) delete trimmedByQuestion[questionId];
      else trimmedByQuestion[questionId] = next;
      dirtyRef.current = true;
      return { byQuestion: trimmedByQuestion };
    });
  }, []);

  const reload = useCallback(async () => {
    if (!attemptId || disabled) return;
    try {
      const fresh = await apiRef.current.getAnnotations(attemptId);
      setState(safeParse(fresh.annotationsJson));
      lastPayloadRef.current = fresh.annotationsJson;
      dirtyRef.current = false;
      setSaveStatus('saved');
    } catch {
      setSaveStatus('error');
    }
  }, [attemptId, disabled]);

  return {
    state,
    saveStatus,
    lastSavedAt,
    tooLarge,
    update,
    /** Force-flush pending edits immediately (e.g. before section advance). */
    flush,
    /** Re-hydrate from the server (useful after a tab reload mid-attempt). */
    reload,
  };
}
