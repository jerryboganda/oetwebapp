'use client';

import { useCallback, useEffect, useState } from 'react';
import { RotateCcw, Volume2 } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import {
  fetchRecallsAudio,
  fetchRecallSpellingMistakes,
  isApiError,
  type RecallsSpellingMistakeItem,
} from '@/lib/api';
import { playTransientAudio } from '@/lib/recalls-audio';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';

export interface ReviewMistakesListProps {
  /**
   * Bump this to force a re-fetch — the page increments it whenever a word is
   * graded, so a word that has just been spelled correctly disappears from the
   * list without a manual refresh.
   */
  refreshToken?: number;
}

/**
 * The learner's persisted "Review Mistakes" list (§3C).
 *
 * These are the words spelled incorrectly in Practice Spelling or the mini
 * Spelling Test. They are stored against the learner on the server, so the list is
 * identical after a logout, an app restart, or on another device — and it is
 * removed automatically once the word is spelled correctly. Only the existing
 * recall word is referenced; no word or audio is duplicated.
 */
export function ReviewMistakesList({ refreshToken = 0 }: ReviewMistakesListProps) {
  const [items, setItems] = useState<RecallsSpellingMistakeItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [playingId, setPlayingId] = useState<string | null>(null);
  const { guardAudio, modal } = useRecallsAudioUpgrade();

  const load = useCallback(async () => {
    try {
      const response = await fetchRecallSpellingMistakes();
      setItems(response.items);
      setError(null);
    } catch {
      setError('Could not load your review mistakes.');
      setItems([]);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, refreshToken]);

  async function play(item: RecallsSpellingMistakeItem) {
    setPlayingId(item.termId);
    try {
      const response = await guardAudio(() => fetchRecallsAudio(item.termId, 'normal'), {
        termId: item.termId,
      });
      if (!response) {
        setPlayingId(null);
        return;
      }
      const audio = playTransientAudio(response.url);
      const stop = () => setPlayingId((current) => (current === item.termId ? null : current));
      if (typeof audio.addEventListener === 'function') {
        audio.addEventListener('ended', stop, { once: true });
        audio.addEventListener('error', stop, { once: true });
      }
      analytics.track('recalls_word_audio_played', { termId: item.termId });
    } catch (err) {
      setPlayingId(null);
      if (!isApiError(err)) setError('Audio is not available for this word yet.');
    }
  }

  if (items === null) {
    return (
      <Card className="border-border bg-surface">
        <Skeleton className="h-20 rounded-xl" />
      </Card>
    );
  }

  return (
    <Card className="border-border bg-surface">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="inline-flex h-8 w-8 items-center justify-center rounded-full bg-warning/10 text-warning">
            <RotateCcw size={15} aria-hidden="true" />
          </span>
          <div>
            <h3 className="text-sm font-semibold text-navy">Review mistakes</h3>
            <p className="text-xs text-muted">
              Words you spelled incorrectly. They clear as soon as you spell them right.
            </p>
          </div>
        </div>
        {items.length > 0 && (
          <span className="rounded-full bg-warning/10 px-2 py-0.5 text-xs font-medium text-warning">
            {items.length}
          </span>
        )}
      </div>

      {error && (
        <p role="alert" className="mt-3 text-xs text-red-600">
          {error}
        </p>
      )}

      {items.length === 0 ? (
        <p className="mt-3 text-sm text-muted">
          Nothing here yet — words you miss in Practice Spelling or a spelling test will appear in
          this list, on every device you sign in to.
        </p>
      ) : (
        <ul className="mt-3 space-y-2">
          {items.map((item) => (
            <li
              key={item.termId}
              className="flex items-center gap-2 rounded-lg border border-border bg-background-light p-3"
            >
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold text-navy">{item.term}</p>
                <p className="mt-0.5 text-xs text-muted">
                  {item.category}
                  {' · '}
                  {item.wrongAttemptCount === 1
                    ? 'missed once'
                    : `missed ${item.wrongAttemptCount} times`}
                </p>
              </div>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void play(item)}
                aria-label={`Play pronunciation of ${item.term}`}
                disabled={!item.hasAudio}
              >
                <Volume2 size={13} className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
      )}

      {modal}
    </Card>
  );
}
