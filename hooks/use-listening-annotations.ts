'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { listeningV2Api } from '@/lib/listening/v2-api';

/**
 * Per-question highlight / strikethrough payload persisted to the backend
 * via `PUT /v1/listening/v2/attempts/{id}/annotations`. The shape is opaque
 * to the server (capped at 64 KB) — feel free to add fields on the frontend
 * as long as they round-trip through JSON.stringify cleanly.
 */
export interface ListeningQuestionAnnotation {
  /** Set of opaque highlight ranges or labels. Frontend defines the schema. */
  highlights?: string[];
  /** Option keys (e.g. "A", "B", "C") the learner has crossed out. */
  struckOptions?: string[];
  /** True when the question stem itself is highlighted. */
  stemHighlighted?: boolean;
}

export interface ListeningAnnotationsState {
  /** Question id → per-question annotation payload. */
  byQuestion: Record<string, ListeningQuestionAnnotation>;
}

const EMPTY_STATE: ListeningAnnotationsState = { byQuestion: {} };
const DEBOUNCE_MS = 400;
const MAX_BYTES = 64 * 1024;

function safeParse(raw: string | null | undefined): ListeningAnnotationsState {
  if (!raw) return EMPTY_STATE;
  try {
    const parsed = JSON.parse(raw) as Partial<ListeningAnnotationsState>;
    if (!parsed || typeof parsed !== 'object' || !parsed.byQuestion) return EMPTY_STATE;
    return { byQuestion: parsed.byQuestion as Record<string, ListeningQuestionAnnotation> };
  } catch {
    return EMPTY_STATE;
  }
}

/**
 * `useListeningAnnotations` — debounced (400 ms) auto-save of highlight +
 * strikethrough state for a Listening V2 attempt. Mirrors the Reading
 * autosave pattern in `app/reading/paper/[paperId]/page.tsx` so behaviour
 * is predictable for the same code reviewer.
 *
 * Hydration: pass the initial payload (from `getState`) on mount; the hook
 * stores a deep copy and only re-fetches if `attemptId` changes. Callers
 * can also invoke `reload()` to re-pull from the server.
 *
 * The save call uses last-write-wins; if the user keeps editing during a
 * pending save, the hook coalesces edits and fires one final PUT.
 */
export function useListeningAnnotations(options: {
  attemptId: string | null;
  initialAnnotationsJson?: string | null;
  disabled?: boolean;
}) {
  const { attemptId, initialAnnotationsJson, disabled } = options;
  const [state, setState] = useState<ListeningAnnotationsState>(() => safeParse(initialAnnotationsJson));
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
      await listeningV2Api.saveAnnotations(attemptId, wirePayload);
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

  const update = useCallback((questionId: string, mutator: (current: ListeningQuestionAnnotation) => ListeningQuestionAnnotation) => {
    setState((prev) => {
      const current = prev.byQuestion[questionId] ?? {};
      const next = mutator(current);
      const trimmedByQuestion = { ...prev.byQuestion };
      const isEmpty = !next.stemHighlighted
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
      const fresh = await listeningV2Api.getAnnotations(attemptId);
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
