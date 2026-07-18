'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { X, Trash2, Loader2, StickyNote } from 'lucide-react';
import { apiClient } from '@/lib/api';

interface NoteDto {
  id: string;
  extractId: string | null;
  transcriptMs: number | null;
  text: string;
  createdAt: string;
  updatedAt: string;
}

export interface NotePanelProps {
  attemptId: string;
  currentExtractId?: string;
  currentPositionMs?: number;
  isOpen: boolean;
  onClose: () => void;
}

function formatTimestamp(transcriptMs: number | null, createdAt: string): string {
  if (transcriptMs != null) {
    const totalSeconds = Math.floor(transcriptMs / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  }
  try {
    return new Date(createdAt).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

export function NotePanel({
  attemptId,
  currentExtractId,
  currentPositionMs,
  isOpen,
  onClose,
}: NotePanelProps) {
  const [notes, setNotes] = useState<NoteDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draftText, setDraftText] = useState('');
  const [saving, setSaving] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingText, setEditingText] = useState('');
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const fetchNotes = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await apiClient.get<NoteDto[]>(
        `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/notes`,
      );
      // Show newest first
      setNotes([...result].reverse());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load notes.');
    } finally {
      setLoading(false);
    }
  }, [attemptId]);

  useEffect(() => {
    if (isOpen) {
      void fetchNotes();
    }
  }, [isOpen, fetchNotes]);

  // Keyboard shortcut: N key toggles (handled by the player page via isOpen/onClose)
  // Here we just focus the textarea when the panel opens
  useEffect(() => {
    if (isOpen) {
      setTimeout(() => textareaRef.current?.focus(), 50);
    }
  }, [isOpen]);

  const handleSave = async () => {
    const text = draftText.trim();
    if (!text) return;
    setSaving(true);
    try {
      const note = await apiClient.post<NoteDto>(
        `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/notes`,
        {
          extractId: currentExtractId ?? null,
          transcriptMs: currentPositionMs ?? null,
          text,
        },
      );
      setDraftText('');
      // Prepend since we show newest first
      setNotes((prev) => [note, ...prev]);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save note.');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (noteId: string) => {
    if (!window.confirm('Delete this note?')) return;
    setDeletingId(noteId);
    try {
      await apiClient.delete<void>(
        `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/notes/${encodeURIComponent(noteId)}`,
      );
      setNotes((prev) => prev.filter((n) => n.id !== noteId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not delete note.');
    } finally {
      setDeletingId(null);
    }
  };

  const startEditing = (note: NoteDto) => {
    setEditingId(note.id);
    setEditingText(note.text);
  };

  const commitEdit = async (noteId: string) => {
    const text = editingText.trim();
    if (!text) {
      setEditingId(null);
      return;
    }
    try {
      const updated = await apiClient.put<NoteDto>(
        `/v1/listening/v2/attempts/${encodeURIComponent(attemptId)}/notes/${encodeURIComponent(noteId)}`,
        { text },
      );
      setNotes((prev) => prev.map((n) => (n.id === noteId ? updated : n)));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update note.');
    } finally {
      setEditingId(null);
    }
  };

  if (!isOpen) return null;

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 z-40 bg-navy/20"
        aria-hidden="true"
        onClick={onClose}
      />

      {/* Panel */}
      <div
        role="dialog"
        aria-label="Attempt notes"
        className="fixed right-0 top-0 z-50 flex h-full w-full max-w-sm flex-col bg-surface pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)] shadow-xl sm:rounded-l-xl"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div className="flex items-center gap-2">
            <StickyNote className="h-5 w-5 text-primary" aria-hidden="true" />
            <h2 className="text-sm font-bold text-navy">My Notes</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close notes panel"
            className="rounded-md p-1 text-muted hover:bg-background-light hover:text-navy"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* New note form */}
        <div className="border-b border-border p-4">
          <textarea
            ref={textareaRef}
            value={draftText}
            onChange={(e) => setDraftText(e.target.value.slice(0, 4096))}
            placeholder="Add a note… (max 500 chars shown)"
            maxLength={4096}
            rows={3}
            className="w-full resize-none rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-primary/40"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
                e.preventDefault();
                void handleSave();
              }
            }}
          />
          <div className="mt-2 flex items-center justify-between">
            <span className="text-xs text-muted">{draftText.length}/500 &bull; Ctrl+Enter to save</span>
            <button
              type="button"
              disabled={saving || draftText.trim().length === 0}
              onClick={() => void handleSave()}
              className="rounded-lg bg-primary px-4 py-1.5 text-sm font-semibold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
            >
              {saving ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                'Save'
              )}
            </button>
          </div>
        </div>

        {/* Notes list */}
        <div className="flex-1 overflow-y-auto">
          {loading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-muted" />
            </div>
          ) : error ? (
            <div className="px-4 py-6 text-center text-sm text-danger">{error}</div>
          ) : notes.length === 0 ? (
            <div className="px-4 py-8 text-center text-sm text-muted">
              No notes yet. Add one above.
            </div>
          ) : (
            <ul className="divide-y divide-border">
              {notes.map((note) => (
                <li key={note.id} className="group px-4 py-3">
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <span className="text-xs font-medium text-muted">
                      {formatTimestamp(note.transcriptMs, note.createdAt)}
                      {note.extractId ? (
                        <span className="ml-1 rounded bg-background-light px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted">
                          extract
                        </span>
                      ) : null}
                    </span>
                    <button
                      type="button"
                      onClick={() => void handleDelete(note.id)}
                      disabled={deletingId === note.id}
                      aria-label="Delete note"
                      className="rounded p-1 text-muted opacity-0 transition-opacity hover:text-danger group-hover:opacity-100 disabled:opacity-50"
                    >
                      {deletingId === note.id ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      ) : (
                        <Trash2 className="h-3.5 w-3.5" />
                      )}
                    </button>
                  </div>

                  {editingId === note.id ? (
                    <textarea
                      autoFocus
                      value={editingText}
                      onChange={(e) => setEditingText(e.target.value.slice(0, 4096))}
                      onBlur={() => void commitEdit(note.id)}
                      onKeyDown={(e) => {
                        if (e.key === 'Escape') {
                          setEditingId(null);
                        } else if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
                          e.preventDefault();
                          void commitEdit(note.id);
                        }
                      }}
                      rows={3}
                      className="w-full resize-none rounded-md border border-primary px-2 py-1 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/40"
                    />
                  ) : (
                    <button
                      type="button"
                      onClick={() => startEditing(note)}
                      className="w-full cursor-text rounded-md p-1 text-left text-sm text-navy hover:bg-background-light"
                    >
                      {note.text}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* Footer hint */}
        <div className="border-t border-border px-4 py-2">
          <p className="text-center text-xs text-muted">Press <kbd className="rounded border border-border px-1 font-mono text-[10px]">N</kbd> to toggle notes</p>
        </div>
      </div>
    </>
  );
}
