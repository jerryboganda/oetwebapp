'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  ArrowLeft,
  CheckCircle2,
  Frown,
  Sparkles,
  Star,
  Trophy,
  Volume2,
} from 'lucide-react';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
import { useAuth } from '@/contexts/auth-context';
import {
  getDueForReview,
  submitPronunciationReview,
  type PronunciationCardDto,
} from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Pronunciation review session — Phase 4 of OET_LISTENING_MODULE_PATHWAY §15.
//
// Pulls the SM-2 due queue (default 20 cards), then steps the learner through
// each one as a flashcard with audio playback. After each card the learner
// rates how well they recognised / produced the pronunciation, the result is
// posted back to the SM-2 endpoint, and the next card is shown.
//
// Quality scale (subset of SM-2 5-point):
//   0 = 😩 didn't catch | 3 = 🤔 hard | 4 = ✓ got it | 5 = ⭐ easy
// Quality 1/2 are intentionally omitted — the UI surfaces only the four
// buttons learners actually need to discriminate between.
// ─────────────────────────────────────────────────────────────────────────────

type Quality = 0 | 3 | 4 | 5;

const QUALITY_BUTTONS: Array<{
  label: string;
  emoji: string;
  quality: Quality;
  description: string;
  className: string;
}> = [
  {
    label: "Didn't catch",
    emoji: '😩',
    quality: 0,
    description: 'Reset interval',
    className:
      'border-rose-200 bg-rose-50 text-rose-700 hover:bg-rose-100 dark:border-rose-900/50 dark:bg-rose-950/30 dark:text-rose-300 dark:hover:bg-rose-900/40',
  },
  {
    label: 'Hard',
    emoji: '🤔',
    quality: 3,
    description: 'Short interval',
    className:
      'border-orange-200 bg-orange-50 text-orange-700 hover:bg-orange-100 dark:border-orange-900/50 dark:bg-orange-950/30 dark:text-orange-300 dark:hover:bg-orange-900/40',
  },
  {
    label: 'Got it',
    emoji: '✓',
    quality: 4,
    description: 'Normal interval',
    className:
      'border-blue-200 bg-blue-50 text-blue-700 hover:bg-blue-100 dark:border-blue-900/50 dark:bg-blue-950/30 dark:text-blue-300 dark:hover:bg-blue-900/40',
  },
  {
    label: 'Easy',
    emoji: '⭐',
    quality: 5,
    description: 'Long interval',
    className:
      'border-emerald-200 bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:border-emerald-900/50 dark:bg-emerald-950/30 dark:text-emerald-300 dark:hover:bg-emerald-900/40',
  },
];

export default function PronunciationReviewPage() {
  const router = useRouter();
  const { isAuthenticated, loading: authLoading } = useAuth();

  const [cards, setCards] = useState<PronunciationCardDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [completed, setCompleted] = useState(0);

  const audioRef = useRef<HTMLAudioElement | null>(null);

  // Fetch the due queue once on mount — capped at 20 so a long session
  // stays scoped to a single sitting.
  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const due = await getDueForReview(20);
        if (!cancelled) setCards(due);
      } catch {
        // Empty queue surfaces as the "all done" screen anyway.
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  const currentCard = cards[currentIndex];
  const total = cards.length;
  const progressPct = total > 0 ? (completed / total) * 100 : 0;

  const audioUrl = useMemo<string | null>(() => {
    if (!currentCard) return null;
    return currentCard.audioBritishUrl ?? currentCard.audioAustralianUrl ?? null;
  }, [currentCard]);

  function handlePlay() {
    if (!audioUrl) return;
    if (!audioRef.current) audioRef.current = new Audio();
    audioRef.current.src = audioUrl;
    audioRef.current.play().catch(() => {
      toast.error('Audio playback failed.');
    });
  }

  async function handleRate(quality: Quality) {
    if (!currentCard || submitting) return;
    setSubmitting(true);
    try {
      await submitPronunciationReview(currentCard.id, quality);
    } catch {
      // Fire-and-forget — don't block the learner on transient network errors.
    } finally {
      setSubmitting(false);
    }

    const nextCompleted = completed + 1;
    setCompleted(nextCompleted);

    if (currentIndex + 1 >= total) {
      // The session is over; the "all done" screen renders below because the
      // current index will overflow the cards array on the next render.
      setCurrentIndex(total);
      return;
    }

    setCurrentIndex((i) => i + 1);
    setRevealed(false);
  }

  function handleFinish() {
    toast.success('Pronunciation session complete!');
    router.push('/listening/pronunciation');
  }

  // ── Render branches ────────────────────────────────────────────────────────

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Pronunciation Review">
        <main className="mx-auto max-w-xl">
          <div className="flex h-64 items-center justify-center">
            <div className="h-10 w-10 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
          </div>
        </main>
      </LearnerDashboardShell>
    );
  }

  // Empty queue → "nothing to review" screen.
  if (total === 0) {
    return (
      <LearnerDashboardShell pageTitle="Pronunciation Review">
        <main className="mx-auto max-w-xl space-y-6">
          <div className="flex items-center justify-between">
            <h1 className="text-xl font-bold text-neutral-900 dark:text-white">Review Session</h1>
            <Link
              href="/listening/pronunciation"
              className="inline-flex items-center gap-1 text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden />
              Back
            </Link>
          </div>
          <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-8 py-12 text-center dark:border-emerald-900/50 dark:bg-emerald-950/30">
            <Sparkles className="mx-auto h-10 w-10 text-emerald-500" aria-hidden />
            <p className="mt-3 text-lg font-semibold text-neutral-900 dark:text-white">
              Nothing to review today!
            </p>
            <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
              Come back tomorrow — SM-2 has scheduled your next session.
            </p>
            <Link
              href="/listening/pronunciation"
              className="mt-5 inline-flex rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-violet-700"
            >
              Back to Library
            </Link>
          </div>
        </main>
      </LearnerDashboardShell>
    );
  }

  // Session complete → confirmation screen.
  if (!currentCard) {
    return (
      <LearnerDashboardShell pageTitle="Pronunciation Review">
        <main className="mx-auto max-w-xl space-y-6">
          <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-8 py-12 text-center dark:border-emerald-900/50 dark:bg-emerald-950/30">
            <Trophy className="mx-auto h-12 w-12 text-emerald-500" aria-hidden />
            <p className="mt-3 text-2xl font-bold text-neutral-900 dark:text-white">All done!</p>
            <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
              You reviewed {completed} {completed === 1 ? 'card' : 'cards'}. SM-2 has scheduled the next
              round for each one.
            </p>
            <div className="mt-6 flex justify-center gap-3">
              <button
                type="button"
                onClick={handleFinish}
                className="inline-flex rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-violet-700"
              >
                Back to Library
              </button>
              <Link
                href="/listening"
                className="inline-flex rounded-xl border border-violet-200 bg-white px-5 py-2.5 text-sm font-semibold text-violet-700 hover:bg-violet-50 dark:border-violet-800/60 dark:bg-neutral-900 dark:text-violet-300"
              >
                Listening Hub
              </Link>
            </div>
          </div>
        </main>
      </LearnerDashboardShell>
    );
  }

  // ── Flashcard view ─────────────────────────────────────────────────────────

  return (
    <LearnerDashboardShell pageTitle="Pronunciation Review">
      <main className="mx-auto max-w-xl space-y-6">
        <div className="flex items-center justify-between">
          <h1 className="text-xl font-bold text-neutral-900 dark:text-white">Pronunciation Review</h1>
          <Link
            href="/listening/pronunciation"
            className="inline-flex items-center gap-1 text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
          >
            <ArrowLeft className="h-4 w-4" aria-hidden />
            Back
          </Link>
        </div>

        {/* Progress bar */}
        <div className="space-y-1">
          <div className="flex justify-between text-xs font-medium text-neutral-500 dark:text-neutral-400">
            <span>
              {completed} of {total} reviewed
            </span>
            <span>{Math.round(progressPct)}%</span>
          </div>
          <div className="h-2 w-full overflow-hidden rounded-full bg-violet-100 dark:bg-violet-900/40">
            <div
              className="h-full rounded-full bg-violet-600 transition-all duration-300"
              style={{ width: `${progressPct}%` }}
            />
          </div>
        </div>

        {/* Flashcard */}
        <article className="rounded-2xl border border-neutral-200 bg-white px-6 py-8 shadow-sm dark:border-neutral-800 dark:bg-neutral-900">
          {/* Audio + Word */}
          <div className="flex flex-col items-center gap-4">
            <button
              type="button"
              disabled={!audioUrl}
              onClick={handlePlay}
              className="group flex h-20 w-20 items-center justify-center rounded-full border-2 border-violet-200 bg-violet-50 text-violet-600 transition hover:scale-105 hover:border-violet-400 hover:bg-violet-100 active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 dark:border-violet-900/50 dark:bg-violet-950/40 dark:text-violet-400 dark:hover:border-violet-700"
              aria-label={`Play pronunciation of ${currentCard.word}`}
            >
              <Volume2 className="h-9 w-9" aria-hidden />
            </button>
            {!audioUrl ? (
              <p className="text-xs text-neutral-400 dark:text-neutral-600">
                Audio asset not yet available
              </p>
            ) : null}

            {revealed ? (
              <div className="w-full text-center">
                <h2 className="text-3xl font-bold text-neutral-900 dark:text-white">
                  {currentCard.word}
                </h2>
                {currentCard.pronunciationIpa ? (
                  <p className="mt-1 font-mono text-base text-violet-600 dark:text-violet-400">
                    {currentCard.pronunciationIpa}
                  </p>
                ) : null}
                {currentCard.definitionEn ? (
                  <p className="mt-3 text-sm text-neutral-600 dark:text-neutral-400">
                    {currentCard.definitionEn}
                  </p>
                ) : null}
              </div>
            ) : (
              <div className="w-full text-center">
                <p className="text-xs uppercase tracking-widest text-neutral-400 dark:text-neutral-600">
                  Listen, then rate yourself
                </p>
                <p className="mt-2 text-lg font-semibold text-neutral-400 dark:text-neutral-600">
                  • • • • •
                </p>
              </div>
            )}
          </div>
        </article>

        {/* Actions */}
        {!revealed ? (
          <button
            type="button"
            onClick={() => setRevealed(true)}
            className="w-full rounded-xl bg-violet-600 px-6 py-3 text-sm font-semibold text-white transition hover:bg-violet-700 active:scale-95"
          >
            Reveal Word
          </button>
        ) : (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {QUALITY_BUTTONS.map(({ label, emoji, quality, description, className }) => (
              <button
                key={quality}
                type="button"
                disabled={submitting}
                onClick={() => void handleRate(quality)}
                className={`flex flex-col items-center gap-1 rounded-xl border px-3 py-3 text-xs font-semibold transition disabled:opacity-50 ${className}`}
              >
                <span className="text-2xl leading-none" aria-hidden>
                  {emoji}
                </span>
                <span className="mt-1">{label}</span>
                <span className="text-[10px] font-normal opacity-70">{description}</span>
              </button>
            ))}
          </div>
        )}

        {/* Footer hint icons (decorative) */}
        <div className="flex justify-center gap-4 text-xs text-neutral-400 dark:text-neutral-600">
          <span className="flex items-center gap-1">
            <Frown className="h-3 w-3" aria-hidden /> Reset
          </span>
          <span className="flex items-center gap-1">
            <CheckCircle2 className="h-3 w-3" aria-hidden /> Advance
          </span>
          <span className="flex items-center gap-1">
            <Star className="h-3 w-3" aria-hidden /> Boost
          </span>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
