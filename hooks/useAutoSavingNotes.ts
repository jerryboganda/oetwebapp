'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { saveSessionNotes } from '@/lib/listening-pathway-api';

const DEBOUNCE_MS = 700;

export interface UseAutoSavingNotesOptions {
  sessionId: string | null | undefined;
  questionId?: string | null;
  initialValue?: string;
  /** Disables network writes — useful for SSR or when the page is not yet hydrated. */
  disabled?: boolean;
}

export interface UseAutoSavingNotesResult {
  value: string;
  setValue: (next: string) => void;
  isSaving: boolean;
  lastSavedAt: Date | null;
  error: Error | null;
  flushNow: () => Promise<void>;
}

/**
 * Debounced auto-save for Listening Part A notes (or any per-session note field).
 *
 * Hits POST /v1/listening-pathway/practice/sessions/{sessionId}/notes ~700ms after
 * the learner stops typing. Tracks lastSavedAt so the UI can show "Saved 4s ago".
 */
export function useAutoSavingNotes({
  sessionId,
  questionId,
  initialValue = '',
  disabled = false,
}: UseAutoSavingNotesOptions): UseAutoSavingNotesResult {
  const [value, setValueState] = useState(initialValue);
  const [isSaving, setIsSaving] = useState(false);
  const [lastSavedAt, setLastSavedAt] = useState<Date | null>(null);
  const [error, setError] = useState<Error | null>(null);

  const pendingTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inFlightController = useRef<AbortController | null>(null);
  const lastSentValue = useRef<string>(initialValue);

  const flushNow = useCallback(async () => {
    if (disabled || !sessionId) return;
    if (value === lastSentValue.current) return;

    inFlightController.current?.abort();
    const controller = new AbortController();
    inFlightController.current = controller;

    setIsSaving(true);
    setError(null);
    try {
      const result = await saveSessionNotes(sessionId, {
        questionId: questionId ?? undefined,
        noteMarkdown: value,
      });
      lastSentValue.current = value;
      const savedAt = result?.savedAt ? new Date(result.savedAt) : new Date();
      setLastSavedAt(savedAt);
    } catch (err) {
      if ((err as { name?: string })?.name === 'AbortError') return;
      setError(err instanceof Error ? err : new Error(String(err)));
    } finally {
      setIsSaving(false);
    }
  }, [disabled, sessionId, questionId, value]);

  const setValue = useCallback((next: string) => {
    setValueState(next);
  }, []);

  useEffect(() => {
    if (disabled || !sessionId) return;
    if (value === lastSentValue.current) return;

    if (pendingTimer.current) clearTimeout(pendingTimer.current);
    pendingTimer.current = setTimeout(() => {
      void flushNow();
    }, DEBOUNCE_MS);

    return () => {
      if (pendingTimer.current) clearTimeout(pendingTimer.current);
    };
  }, [value, sessionId, disabled, flushNow]);

  useEffect(() => {
    return () => {
      inFlightController.current?.abort();
    };
  }, []);

  return { value, setValue, isSaving, lastSavedAt, error, flushNow };
}
