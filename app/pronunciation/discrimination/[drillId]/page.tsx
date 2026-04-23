'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, Headphones, RotateCcw, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  fetchPronunciationDrill,
  submitPronunciationDiscrimination,
  type PronunciationDrillSummary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

type MinimalPair = { a: string; b: string };

type Round = {
  pair: MinimalPair;
  correctAnswer: 'a' | 'b';
  userAnswer: 'a' | 'b' | null;
  pairIndex: number;
};

/**
 * Listening discrimination game. For each round the learner:
 *   1. Plays one of the two words via the browser Speech Synthesis API
 *      (the words are deterministic — a real content team can upload MP3s per
 *      pair and swap those in; the SS API is a universal fallback so the game
 *      works out of the box with the seeded text-only drills).
 *   2. Clicks which of the two words they think they heard.
 * After N rounds the aggregate accuracy is submitted to the server.
 */
export default function PronunciationDiscriminationPage() {
  const params = useParams<{ drillId: string }>();
  const drillId = params?.drillId ?? '';

  const [drill, setDrill] = useState<PronunciationDrillSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [rounds, setRounds] = useState<Round[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [done, setDone] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const pairsRef = useRef<MinimalPair[]>([]);

  // Load drill + build rounds
  useEffect(() => {
    if (!drillId) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      try {
        const d = (await fetchPronunciationDrill(drillId)) as PronunciationDrillSummary;
        if (cancelled) return;
        setDrill(d);
        const pairs = parsePairs(d.minimalPairsJson);
        pairsRef.current = pairs;
        if (pairs.length === 0) {
          setError('This drill has no minimal pairs to practise.');
          setLoading(false);
          return;
        }
        analytics.track('pronunciation_discrimination_started', { drillId });
        setRounds(buildRounds(pairs, 8));
        setCurrentIndex(0);
        setDone(false);
        setLoading(false);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load the drill.');
        setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [drillId]);

  const speak = useCallback((word: string) => {
    if (typeof window === 'undefined') return;
    const synth = window.speechSynthesis;
    if (!synth) return;
    synth.cancel();
    const utter = new SpeechSynthesisUtterance(word);
    utter.lang = 'en-GB';
    utter.rate = 0.9;
    synth.speak(utter);
  }, []);

  const currentRound = rounds[currentIndex];
  const playCurrent = useCallback(() => {
    if (!currentRound) return;
    const target = currentRound.correctAnswer === 'a' ? currentRound.pair.a : currentRound.pair.b;
    speak(target);
  }, [currentRound, speak]);

  const chooseAnswer = useCallback((answer: 'a' | 'b') => {
    setRounds((prev) => {
      const next = [...prev];
      next[currentIndex] = { ...next[currentIndex], userAnswer: answer };
      return next;
    });
    setTimeout(() => {
      if (currentIndex + 1 >= rounds.length) {
        setDone(true);
      } else {
        setCurrentIndex((i) => i + 1);
      }
    }, 350);
  }, [currentIndex, rounds.length]);

  useEffect(() => {
    // Auto-play the first round when ready, and each new round
    if (!currentRound || done) return;
    const t = setTimeout(() => playCurrent(), 200);
    return () => clearTimeout(t);
  }, [currentRound, done, playCurrent]);

  const stats = useMemo(() => {
    const total = rounds.length;
    const answered = rounds.filter((r) => r.userAnswer !== null).length;
    const correct = rounds.filter((r) => r.userAnswer === r.correctAnswer).length;
    return { total, answered, correct };
  }, [rounds]);

  const handleSubmit = useCallback(async () => {
    if (!drill) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await submitPronunciationDiscrimination(drill.id, stats.total, stats.correct);
      analytics.track('pronunciation_discrimination_completed', {
        drillId: drill.id,
        roundsCorrect: stats.correct,
        roundsTotal: stats.total,
      });
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'Could not submit your results.');
    } finally {
      setSubmitting(false);
    }
  }, [drill, stats]);

  const handleRetry = useCallback(() => {
    if (pairsRef.current.length === 0) return;
    setRounds(buildRounds(pairsRef.current, 8));
    setCurrentIndex(0);
    setDone(false);
    setSubmitError(null);
  }, []);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-8 w-48 mb-4" />
        <Skeleton className="h-64 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!drill || error) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Drill not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link
          href={`/pronunciation/${drill.id}`}
          aria-label="Back to drill"
          className="text-muted/60 hover:text-muted"
        >
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <h1 className="text-2xl font-bold text-navy">
          Listening discrimination — {drill.label}
        </h1>
      </div>

      <div className="max-w-2xl mx-auto space-y-6">
        <LearnerPageHero
          eyebrow="Game"
          title="Which one did you hear?"
          description="Train your ear to distinguish the target phoneme from its closest contrast. Listen carefully, then pick A or B."
          icon={Headphones}
          highlights={[
            { icon: Headphones, label: 'Rounds', value: `${stats.answered}/${stats.total}` },
            { icon: Volume2, label: 'Accuracy', value: stats.answered > 0 ? `${Math.round((stats.correct / stats.answered) * 100)}%` : '—' },
            { icon: Volume2, label: 'Phoneme', value: `/${drill.targetPhoneme}/` },
          ]}
        />

        {!done && currentRound && (
          <section aria-labelledby="round-heading" className="rounded-3xl border border-border bg-surface p-6 shadow-sm text-center">
            <h2 id="round-heading" className="mb-4 text-sm uppercase tracking-[0.18em] text-muted">
              Round {currentIndex + 1} of {rounds.length}
            </h2>
            <Button
              variant="primary"
              onClick={playCurrent}
              aria-label="Play the word again"
              className="mb-6 gap-2"
            >
              <Volume2 className="h-4 w-4" /> Play again
            </Button>
            <div className="grid grid-cols-2 gap-4">
              <DiscriminationChoice
                label="A"
                word={currentRound.pair.a}
                onSelect={() => chooseAnswer('a')}
                disabled={currentRound.userAnswer !== null}
                selected={currentRound.userAnswer === 'a'}
                correct={currentRound.userAnswer !== null && currentRound.correctAnswer === 'a'}
              />
              <DiscriminationChoice
                label="B"
                word={currentRound.pair.b}
                onSelect={() => chooseAnswer('b')}
                disabled={currentRound.userAnswer !== null}
                selected={currentRound.userAnswer === 'b'}
                correct={currentRound.userAnswer !== null && currentRound.correctAnswer === 'b'}
              />
            </div>
          </section>
        )}

        {done && (
          <section aria-labelledby="results-heading" className="rounded-3xl border border-border bg-surface p-6 shadow-sm text-center">
            <h2 id="results-heading" className="mb-2 text-lg font-semibold text-navy">
              You got {stats.correct} / {stats.total} correct
            </h2>
            <p className="mb-4 text-sm text-muted">
              {stats.correct === stats.total
                ? 'Perfect — your ear is tuned to this contrast.'
                : stats.correct >= stats.total * 0.75
                  ? 'Strong. A few more rounds to lock it in.'
                  : 'Focused practice pays off. Try the drill again, then record the minimal-pair words yourself.'}
            </p>
            {submitError && <InlineAlert variant="warning" className="mb-3">{submitError}</InlineAlert>}
            <div className="flex flex-wrap justify-center gap-3">
              <Button variant="primary" onClick={handleSubmit} disabled={submitting}>
                {submitting ? 'Saving…' : 'Save result'}
              </Button>
              <Button variant="ghost" onClick={handleRetry} className="gap-2">
                <RotateCcw className="h-4 w-4" /> Play again
              </Button>
              <Link
                href={`/pronunciation/${drill.id}`}
                className="inline-flex items-center rounded-2xl border border-border px-4 py-2 text-sm font-medium hover:bg-background-light"
              >
                Back to drill
              </Link>
            </div>
          </section>
        )}
      </div>
    </LearnerDashboardShell>
  );
}

function DiscriminationChoice({
  label,
  word,
  onSelect,
  disabled,
  selected,
  correct,
}: {
  label: string;
  word: string;
  onSelect: () => void;
  disabled: boolean;
  selected: boolean;
  correct: boolean;
}) {
  const base = 'rounded-3xl border-2 p-6 transition focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2';
  const state = !disabled
    ? 'border-border bg-surface hover:border-primary'
    : selected
      ? (correct ? 'border-success bg-success/10' : 'border-danger bg-danger/10')
      : correct
        ? 'border-success bg-success/10'
        : 'border-border bg-surface opacity-60';
  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className={`${base} ${state}`}
      aria-label={`Choose ${label}: ${word}`}
    >
      <div className="text-xs uppercase tracking-[0.18em] text-muted">{label}</div>
      <div className="mt-1 text-2xl font-semibold text-navy">{word}</div>
    </button>
  );
}

function parsePairs(json: string | null | undefined): MinimalPair[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((p): p is MinimalPair => {
      if (!p || typeof p !== 'object') return false;
      const o = p as Record<string, unknown>;
      return typeof o.a === 'string' && typeof o.b === 'string';
    });
  } catch { return []; }
}

function buildRounds(pairs: MinimalPair[], count: number): Round[] {
  const result: Round[] = [];
  for (let i = 0; i < count; i++) {
    const pairIndex = i % pairs.length;
    const pair = pairs[pairIndex];
    const correctAnswer: 'a' | 'b' = Math.random() < 0.5 ? 'a' : 'b';
    result.push({ pair, correctAnswer, userAnswer: null, pairIndex });
  }
  // Shuffle
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}
