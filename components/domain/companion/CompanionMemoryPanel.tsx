'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslations } from 'next-intl';
import { Trash2 } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  deleteCompanionBookmark,
  deleteCompanionNote,
  fetchCompanionMemory,
  resetCompanionMemory,
} from '@/lib/api/companion';

const MEMORY_KEY = ['companion', 'memory'] as const;

/**
 * F-047 — companion memory controls.
 *
 * The companion's `save_user_note` and `bookmark_recall_term` tools write rows
 * on the learner's behalf. This is where the learner sees exactly what was
 * saved and removes any of it. Deletion is server-scoped by user id, so the
 * panel cannot be used to reach anyone else's rows.
 */
export function CompanionMemoryPanel() {
  const t = useTranslations();
  const queryClient = useQueryClient();

  const memory = useQuery({
    queryKey: MEMORY_KEY,
    queryFn: fetchCompanionMemory,
    staleTime: 30_000,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: MEMORY_KEY });
  };

  const removeNote = useMutation({ mutationFn: deleteCompanionNote, onSuccess: invalidate });
  const removeBookmark = useMutation({ mutationFn: deleteCompanionBookmark, onSuccess: invalidate });
  const reset = useMutation({ mutationFn: resetCompanionMemory, onSuccess: invalidate });

  const noteCount = memory.data?.notes.length ?? 0;
  const bookmarkCount = memory.data?.bookmarks.length ?? 0;
  const isEmpty = !memory.isLoading && noteCount === 0 && bookmarkCount === 0;

  return (
    <Card padding="md">
      <div className="flex items-start justify-between gap-2">
        <h2 className="text-sm font-semibold text-navy">{t('companion.memory.title')}</h2>
        {!isEmpty && !memory.isLoading && (
          <button
            type="button"
            onClick={() => reset.mutate()}
            disabled={reset.isPending}
            className="text-xs text-muted underline transition-colors hover:text-navy disabled:opacity-50"
          >
            {t('companion.memory.reset')}
          </button>
        )}
      </div>

      <p className="mt-1 text-xs text-muted">{t('companion.memory.description')}</p>

      {memory.isLoading && <Skeleton className="mt-3 h-16 w-full" />}

      {isEmpty && <p className="mt-3 text-xs text-muted">{t('companion.memory.empty')}</p>}

      {noteCount > 0 && (
        <ul className="mt-3 space-y-2" data-testid="companion-memory-notes">
          {memory.data?.notes.map((note) => (
            <li key={note.id} className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="truncate text-xs font-semibold text-navy">{note.title}</p>
                <p className="line-clamp-2 text-xs text-muted">{note.body}</p>
              </div>
              <button
                type="button"
                onClick={() => removeNote.mutate(note.id)}
                disabled={removeNote.isPending}
                aria-label={t('companion.memory.deleteNote', { title: note.title })}
                className="shrink-0 rounded p-1 text-muted transition-colors hover:bg-background-light hover:text-danger disabled:opacity-50"
              >
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}

      {bookmarkCount > 0 && (
        <ul className="mt-3 flex flex-wrap gap-1.5" data-testid="companion-memory-bookmarks">
          {memory.data?.bookmarks.map((bookmark) => (
            <li key={bookmark.id}>
              <button
                type="button"
                onClick={() => removeBookmark.mutate(bookmark.id)}
                disabled={removeBookmark.isPending}
                aria-label={t('companion.memory.deleteBookmark', { term: bookmark.term })}
                className="inline-flex items-center gap-1 rounded-full border border-border px-2 py-0.5 text-xs text-navy transition-colors hover:border-danger hover:text-danger disabled:opacity-50"
              >
                {bookmark.term}
                <Trash2 className="h-3 w-3" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
