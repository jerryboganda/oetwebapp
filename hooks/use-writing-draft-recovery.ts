'use client';

import { useCallback, useMemo } from 'react';

export interface WritingRecoveredDraft {
  content: string;
  updatedAt: string;
}

interface StoredWritingDraft extends WritingRecoveredDraft {
  taskId: string;
  mode: string;
}

export function useWritingDraftRecovery(taskId: string, mode: string) {
  const storageKey = useMemo(() => `writing_draft_recovery:${mode}:${taskId}`, [mode, taskId]);

  const loadRecoveredDraft = useCallback((): WritingRecoveredDraft | null => {
    if (!taskId || typeof window === 'undefined') return null;

    try {
      const raw = window.localStorage.getItem(storageKey);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as Partial<StoredWritingDraft>;
      if (parsed.taskId !== taskId || parsed.mode !== mode || typeof parsed.content !== 'string') {
        return null;
      }
      return {
        content: parsed.content,
        updatedAt: typeof parsed.updatedAt === 'string' ? parsed.updatedAt : new Date().toISOString(),
      };
    } catch {
      return null;
    }
  }, [mode, storageKey, taskId]);

  const saveRecoveredDraft = useCallback((content: string) => {
    if (!taskId || typeof window === 'undefined') return;

    const trimmed = content.trim();
    if (!trimmed) {
      window.localStorage.removeItem(storageKey);
      return;
    }

    const payload: StoredWritingDraft = {
      taskId,
      mode,
      content,
      updatedAt: new Date().toISOString(),
    };

    try {
      window.localStorage.setItem(storageKey, JSON.stringify(payload));
    } catch {
      // Browser storage is best-effort; server autosave remains authoritative.
    }
  }, [mode, storageKey, taskId]);

  const clearRecoveredDraft = useCallback(() => {
    if (!taskId || typeof window === 'undefined') return;

    try {
      window.localStorage.removeItem(storageKey);
    } catch {
      // Ignore storage cleanup failures.
    }
  }, [storageKey, taskId]);

  return { loadRecoveredDraft, saveRecoveredDraft, clearRecoveredDraft };
}