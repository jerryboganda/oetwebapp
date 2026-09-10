'use client';

import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { CheckCircle2, RotateCcw, SpellCheck, Volume2, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Card } from '@/components/ui/card';
import {
  checkRecallSpelling,
  fetchRecallsAudio,
  fetchRecallSpellingSet,
  isApiError,
  type RecallsSpellingSetItem,
  type RecallsSpellingTestSize,
  type RecallsSpellingTestSource,
} from '@/lib/api';
import { playTransientAudio } from '@/lib/recalls-audio';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';

/** One graded answer, kept so the end screen can review the misses. */
interface TestAnswer {
  termId: string;
  correct: boolean;
  canonical: string;
  typed: string;
}

const SIZE_OPTIONS: { key: RecallsSpellingTestSize; label: string }[] = [
  { key: '10', label: '10' },
  { key: '20', label: '20' },
  { key: '30', label: '30' },
  { key: 'all', label: 'All Words' },
];

const SOURCE_OPTIONS: { key: RecallsSpellingTestSource; label: string; hint: string }[] = [
  // Labelled "Whole bank" rather than "All Words" so it cannot be confused with
  // the "All Words" size above — two controls sharing one name is ambiguous for
  // screen readers and for tests.
  { key: 'all', label: 'Whole bank', hint: 'Every recall word that has audio.' },
  { key: 'favorites', label: 'Favourites', hint: 'Only the words you have hearted.' },
  { key: 'mistakes', label: 'Review Mistakes', hint: 'Words you have spelled incorrectly before.' },
];

export interface SpellingTestProps {
  /** Lets the page refresh its Review Mistakes list after a test. */
  onMistakesChanged?: () => void;
}

/**
 * Mini Spelling Test (§3C).
 *
 * Sizes 10 / 20 / 30 / All Words, plus quick sets from Favourites and Review
 * Mistakes. Each word auto-plays its existing audio, the answer stays hidden, and
 * the canonical spelling is revealed only after Check. The end screen reports
 * total correct, total questions and the percentage, and can list the missed
 * words. There is no timer.
 *
 * Grading happens server-side against the stored canonical word, so this surface
 * costs no AI credits and reuses the existing audio (§3D).
 */
export function SpellingTest({ onMistakesChanged }: SpellingTestProps) {
  const [size, setSize] = useState<RecallsSpellingTestSize>('10');
  const [source, setSource] = useState<RecallsSpellingTestSource>('all');
  const [phase, setPhase] = useState<'setup' | 'running' | 'results'>('setup');
  const [items, setItems] = useState<RecallsSpellingSetItem[]>([]);
  const [index, setIndex] = useState(0);
  const [answers, setAnswers] = useState<TestAnswer[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reviewOpen, setReviewOpen] = useState(false);

  const current = items[index];

  const start = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const set = await fetchRecallSpellingSet(size, source);
      if (set.items.length === 0) {
        setError(
          source === 'mistakes'
            ? 'No mistakes to review yet — miss a word in Practice Spelling first.'
            : source === 'favorites'
              ? 'You have not favourited any words with audio yet.'
              : 'No recall words with audio are available yet.',
        );
        return;
      }
      setItems(set.items);
      setIndex(0);
      setAnswers([]);
      setReviewOpen(false);
      setPhase('running');
      analytics.track('recalls_spelling_test_started', {
        source,
        size,
        questions: set.items.length,
      });
    } catch {
      setError('Could not start the spelling test. Please try again.');
    } finally {
      setLoading(false);
    }
  }, [size, source]);

  function reset() {
    setPhase('setup');
    setItems([]);
    setIndex(0);
    setAnswers([]);
    setReviewOpen(false);
    setError(null);
  }

  const correctCount = useMemo(() => answers.filter((a) => a.correct).length, [answers]);
  const total = answers.length;
  const percentage = total > 0 ? Math.round((correctCount / total) * 100) : 0;
  const incorrect = useMemo(() => answers.filter((a) => !a.correct), [answers]);

  return (
    <>
      <Card className="border-border bg-surface">
        <div className="flex items-start gap-3">
          <span className="inline-flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
            <SpellCheck size={18} aria-hidden="true" />
          </span>
          <div className="min-w-0">
            <h3 className="text-sm font-semibold text-navy">Mini spelling test</h3>
            <p className="mt-0.5 text-xs text-muted">
              Hear a word, type it, get an instant result. No timer.
            </p>
          </div>
        </div>

        <div className="mt-4">
          <span className="text-[11px] font-semibold uppercase tracking-wide text-muted">
            How many words
          </span>
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {SIZE_OPTIONS.map((option) => (
              <button
                key={option.key}
                type="button"
                onClick={() => setSize(option.key)}
                aria-pressed={size === option.key}
                className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                  size === option.key
                    ? 'border-primary bg-primary/10 text-primary'
                    : 'border-border text-muted hover:border-primary/30 hover:text-primary'
                }`}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-3">
          <span className="text-[11px] font-semibold uppercase tracking-wide text-muted">
            Which words
          </span>
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {SOURCE_OPTIONS.map((option) => (
              <button
                key={option.key}
                type="button"
                onClick={() => setSource(option.key)}
                aria-pressed={source === option.key}
                title={option.hint}
                className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                  source === option.key
                    ? 'border-primary bg-primary/10 text-primary'
                    : 'border-border text-muted hover:border-primary/30 hover:text-primary'
                }`}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        {error && (
          <p role="alert" className="mt-3 text-xs text-red-600">
            {error}
          </p>
        )}

        <div className="mt-4">
          <Button size="sm" onClick={() => void start()} disabled={loading}>
            {loading ? 'Preparing…' : 'Start spelling test'}
          </Button>
        </div>
      </Card>

      <Modal
        open={phase !== 'setup'}
        onClose={reset}
        title={phase === 'results' ? 'Spelling test results' : 'Spelling test'}
        size="md"
      >
        {phase === 'running' && current && (
          <RunningStep
            key={current.termId}
            item={current}
            position={index + 1}
            total={items.length}
            onGraded={(answer) => {
              setAnswers((prev) => [...prev, answer]);
              onMistakesChanged?.();
            }}
            onNext={() => {
              if (index + 1 >= items.length) {
                analytics.track('recalls_spelling_test_completed', {
                  source,
                  size,
                  questions: items.length,
                });
                setPhase('results');
              } else {
                setIndex((i) => i + 1);
              }
            }}
          />
        )}

        {phase === 'results' && (
          <div className="space-y-4">
            <div className="rounded-xl border border-border bg-background-light p-4 text-center">
              <p className="text-3xl font-bold text-navy">{percentage}%</p>
              <p className="mt-1 text-sm text-muted">
                {correctCount} correct out of {total} question{total === 1 ? '' : 's'}
              </p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setReviewOpen((open) => !open)}
                disabled={incorrect.length === 0}
              >
                {reviewOpen ? 'Hide incorrect words' : `Review incorrect words (${incorrect.length})`}
              </Button>
              <Button size="sm" onClick={reset}>
                Start another test
              </Button>
            </div>

            {reviewOpen && incorrect.length > 0 && (
              <ul className="space-y-2">
                {incorrect.map((answer) => (
                  <li
                    key={answer.termId}
                    className="rounded-lg border border-border bg-surface p-3 text-sm"
                  >
                    <p className="font-semibold text-navy">{answer.canonical}</p>
                    <p className="mt-0.5 text-xs text-muted">
                      You typed: <span className="italic">{answer.typed || '(nothing)'}</span>
                    </p>
                  </li>
                ))}
              </ul>
            )}

            {incorrect.length === 0 && (
              <p className="text-sm text-muted">Every word was spelled correctly. Excellent.</p>
            )}
          </div>
        )}
      </Modal>
    </>
  );
}

interface RunningStepProps {
  item: RecallsSpellingSetItem;
  position: number;
  total: number;
  onGraded: (answer: TestAnswer) => void;
  onNext: () => void;
}

function RunningStep({ item, position, total, onGraded, onNext }: RunningStepProps) {
  const [typed, setTyped] = useState('');
  const [result, setResult] = useState<{ correct: boolean; canonical: string } | null>(null);
  const [checking, setChecking] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const inputId = useId();
  const { guardAudio, modal } = useRecallsAudioUpgrade();

  const replay = useCallback(async () => {
    setPlaying(true);
    try {
      const response = await guardAudio(() => fetchRecallsAudio(item.termId, 'normal'), {
        termId: item.termId,
      });
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
    } catch {
      setPlaying(false);
      setError('Audio is not available for this word yet.');
    }
  }, [guardAudio, item.termId]);

  // §3C: each word's audio plays automatically when it becomes the current word.
  useEffect(() => {
    void replay();
    inputRef.current?.focus();
  }, [replay]);

  async function handleCheck() {
    if (checking || result) return;
    setChecking(true);
    setError(null);
    try {
      const outcome = await checkRecallSpelling(item.termId, typed);
      setResult({ correct: outcome.correct, canonical: outcome.canonical });
      onGraded({
        termId: item.termId,
        correct: outcome.correct,
        canonical: outcome.canonical,
        typed,
      });
    } catch (err) {
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        setError('Spelling tests are part of a paid plan for this word.');
      } else {
        setError('Could not check that answer. Please try again.');
      }
    } finally {
      setChecking(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-medium text-muted">
          Word {position} of {total}
        </span>
        {item.fromMistakes && (
          <span className="rounded-full bg-warning/10 px-2 py-0.5 text-[11px] font-medium text-warning">
            Review mistake
          </span>
        )}
      </div>

      <button
        type="button"
        onClick={() => void replay()}
        className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-3 py-1.5 text-xs font-medium text-primary transition-colors hover:bg-primary/20"
      >
        <Volume2 size={13} className="h-3.5 w-3.5" aria-hidden="true" />
        {playing ? 'Playing…' : 'Replay Audio'}
      </button>

      <form
        className="flex flex-col gap-2 sm:flex-row"
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
        <p role="alert" className="text-xs text-red-600">
          {error}
        </p>
      )}

      {result && (
        <div role="status" className="rounded-lg border border-border bg-background-light p-3">
          {result.correct ? (
            <p className="flex items-center gap-1.5 text-sm font-semibold text-success">
              <CheckCircle2 size={15} className="h-4 w-4" aria-hidden="true" />
              Correct
            </p>
          ) : (
            <p className="flex items-center gap-1.5 text-sm font-semibold text-red-600">
              <XCircle size={15} className="h-4 w-4" aria-hidden="true" />
              Incorrect
            </p>
          )}
          <p className="mt-1 text-sm text-navy">
            <span className="text-muted">Correct spelling: </span>
            <span className="font-semibold">{result.canonical}</span>
          </p>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {result ? (
          <Button size="sm" onClick={onNext}>
            {position >= total ? 'See results' : 'Next Word'}
          </Button>
        ) : (
          <Button variant="secondary" size="sm" onClick={() => void replay()}>
            <RotateCcw size={13} className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
            Replay Audio
          </Button>
        )}
      </div>

      {modal}
    </div>
  );
}
