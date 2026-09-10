'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { CheckCircle2, RotateCcw, Volume2, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  checkRecallSpelling,
  fetchRecallsAudio,
  isApiError,
  type RecallsSpellingCheckResponse,
} from '@/lib/api';
import { playTransientAudio } from '@/lib/recalls-audio';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';

export interface PracticeSpellingProps {
  /** Recall word id. The canonical spelling stays on the server. */
  termId: string;
  /**
   * Controlled open state. Omit to let the panel manage itself; pass it when the
   * owner needs to drive the flow (e.g. "Next Word" opening the following card).
   */
  open?: boolean;
  /** Fires on open and close, so the owning card can hide the target word. */
  onOpenChange?: (open: boolean) => void;
  /** Fired after every graded answer so the caller can refresh mistake counts. */
  onAnswered?: (result: RecallsSpellingCheckResponse) => void;
  /** Adds a "Next Word" affordance. */
  onNext?: () => void;
}

/**
 * Practice Spelling (§3B) — an expandable panel on a vocabulary card.
 *
 * When open the learner sees no target word and no example sentence: they hear
 * the existing pronunciation and type what they hear. Nothing is generated here —
 * the audio is the stored ElevenLabs asset, and grading is a direct comparison
 * against the stored canonical word, so a spelling answer never costs AI credits
 * (§3D).
 *
 * Note the component never receives the canonical word as a prop — it is only
 * ever shown as `result.canonical`, which the server returns after Check. There is
 * therefore no code path that can render the answer early.
 *
 * The panel is a separate component mounted only while open, so every open starts
 * from a clean slate without a reset effect.
 */
export function PracticeSpelling({
  termId,
  open,
  onOpenChange,
  onAnswered,
  onNext,
}: PracticeSpellingProps) {
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false);
  const isOpen = open ?? uncontrolledOpen;

  const setOpen = useCallback(
    (next: boolean) => {
      if (open === undefined) setUncontrolledOpen(next);
      onOpenChange?.(next);
    },
    [open, onOpenChange],
  );

  if (!isOpen) {
    return (
      <div className="mt-3">
        <Button variant="secondary" size="sm" onClick={() => setOpen(true)}>
          Practice Spelling
        </Button>
      </div>
    );
  }

  return (
    <PracticeSpellingPanel
      termId={termId}
      onClose={() => setOpen(false)}
      onAnswered={onAnswered}
      onNext={onNext}
    />
  );
}

interface PracticeSpellingPanelProps {
  termId: string;
  onClose: () => void;
  onAnswered?: (result: RecallsSpellingCheckResponse) => void;
  onNext?: () => void;
}

function PracticeSpellingPanel({ termId, onClose, onAnswered, onNext }: PracticeSpellingPanelProps) {
  const [typed, setTyped] = useState('');
  const [result, setResult] = useState<RecallsSpellingCheckResponse | null>(null);
  const [checking, setChecking] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const inputId = useId();
  const { guardAudio, modal } = useRecallsAudioUpgrade();

  const replay = useCallback(async () => {
    setPlaying(true);
    try {
      const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
      if (!response) {
        setPlaying(false);
        return;
      }
      const audio = playTransientAudio(response.url);
      const stop = () => setPlaying(false);
      if (typeof audio.addEventListener === 'function') {
        audio.addEventListener('ended', stop, { once: true });
        audio.addEventListener('error', stop, { once: true });
      }
      analytics.track('recalls_word_audio_played', { termId });
    } catch {
      setPlaying(false);
      setError('Audio is not available for this word yet.');
    }
  }, [guardAudio, termId]);

  // §3B: the word's audio plays automatically when the panel opens. Replays are
  // the learner's choice — the bytes come from the client cache, so nothing is
  // re-generated or re-charged.
  useEffect(() => {
    void replay();
    inputRef.current?.focus();
  }, [replay]);

  async function handleCheck() {
    if (checking || result) return;
    setChecking(true);
    setError(null);
    try {
      const outcome = await checkRecallSpelling(termId, typed);
      setResult(outcome);
      analytics.track('recalls_spelling_practised', { termId, correct: outcome.correct });
      onAnswered?.(outcome);
    } catch (err) {
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        setError('Spelling practice on this word is part of a paid plan.');
      } else {
        setError('Could not check that answer. Please try again.');
      }
    } finally {
      setChecking(false);
    }
  }

  function handleTryAgain() {
    setTyped('');
    setResult(null);
    setError(null);
    void replay();
    inputRef.current?.focus();
  }

  return (
    <div className="mt-3 rounded-xl border border-border bg-background-light p-3">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[11px] font-semibold uppercase tracking-wide text-muted">
          Practice spelling
        </span>
        <button
          type="button"
          onClick={onClose}
          className="text-xs text-muted underline-offset-2 hover:underline"
        >
          Close
        </button>
      </div>

      <p className="mt-2 text-xs text-muted">Listen, then type the word you hear.</p>

      <button
        type="button"
        onClick={() => void replay()}
        aria-label="Replay audio"
        className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-3 py-1 text-xs font-medium text-primary transition-colors hover:bg-primary/20"
      >
        <Volume2 size={13} strokeWidth={2} className="h-3.5 w-3.5" aria-hidden="true" />
        {playing ? 'Playing…' : 'Replay audio'}
      </button>

      <form
        className="mt-3 flex flex-col gap-2 sm:flex-row"
        onSubmit={(event) => {
          event.preventDefault();
          void handleCheck();
        }}
      >
        <label htmlFor={inputId} className="sr-only">
          Type the word you hear
        </label>
        <input
          id={inputId}
          ref={inputRef}
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          disabled={Boolean(result) || checking}
          placeholder="Type the word you hear"
          // Autocorrect and the spell checker would give the answer away.
          autoComplete="off"
          autoCorrect="off"
          autoCapitalize="off"
          spellCheck={false}
          className="min-w-0 flex-1 rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy outline-none focus-visible:border-primary focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-70"
        />
        <Button type="submit" size="sm" disabled={checking || Boolean(result) || typed.length === 0}>
          {checking ? 'Checking…' : 'Check'}
        </Button>
      </form>

      {error && (
        <p role="alert" className="mt-2 text-xs text-red-600">
          {error}
        </p>
      )}

      {result && (
        <div role="status" className="mt-3 rounded-lg border border-border bg-surface p-3">
          {result.correct ? (
            <p className="flex items-center gap-1.5 text-sm font-semibold text-success">
              <CheckCircle2 size={15} className="h-4 w-4" aria-hidden="true" />
              Correct spelling
            </p>
          ) : (
            <p className="flex items-center gap-1.5 text-sm font-semibold text-red-600">
              <XCircle size={15} className="h-4 w-4" aria-hidden="true" />
              Incorrect
            </p>
          )}
          {/* The canonical spelling is revealed only after Check. */}
          <p className="mt-1 text-sm text-navy">
            <span className="text-muted">Correct spelling: </span>
            <span className="font-semibold">{result.canonical}</span>
          </p>
          {!result.correct && (
            <p className="mt-1 text-xs text-muted">
              {result.wrongAttemptCount > 1
                ? `Added to Review Mistakes — missed ${result.wrongAttemptCount} times.`
                : 'Added to Review Mistakes so you can practise it again.'}
            </p>
          )}
          {result.correct && result.removedFromMistakes && (
            <p className="mt-1 text-xs text-muted">Removed from Review Mistakes. Well done.</p>
          )}
        </div>
      )}

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Button variant="secondary" size="sm" onClick={handleTryAgain}>
          <RotateCcw size={13} className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
          Try Again
        </Button>
        {onNext && (
          <Button variant="ghost" size="sm" onClick={onNext}>
            Next Word
          </Button>
        )}
      </div>

      {modal}
    </div>
  );
}
