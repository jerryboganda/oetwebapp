'use client';

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  CheckCircle2,
  Frown,
  Headphones,
  Sparkles,
  Star,
  Trophy,
  Volume2,
} from 'lucide-react';
import { toast } from 'sonner';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
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
      'border-danger/20 bg-danger/10 text-danger-strong hover:bg-danger/20',
  },
  {
    label: 'Hard',
    emoji: '🤔',
    quality: 3,
    description: 'Short interval',
    className:
      'border-warning/20 bg-warning/10 text-warning-strong hover:bg-warning/20',
  },
  {
    label: 'Got it',
    emoji: '✓',
    quality: 4,
    description: 'Normal interval',
    className:
      'border-info/20 bg-info/10 text-info hover:bg-info/20',
  },
  {
    label: 'Easy',
    emoji: '⭐',
    quality: 5,
    description: 'Long interval',
    className:
      'border-success/20 bg-success/10 text-success-strong hover:bg-success/20',
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
  // One header for every state (the empty and session states had two different
  // h1s and the done state had none); the breadcrumb's "Pronunciation" crumb is
  // the way back. The flashcard keeps a narrow column inside the page frame.

  let session: ReactNode;

  if (loading) {
    session = (
      <div className="space-y-6">
        <Skeleton className="h-2 w-full rounded-full" />
        <Skeleton className="h-64 w-full rounded-2xl" />
      </div>
    );
  } else if (total === 0) {
    // Empty queue → "nothing to review" screen.
    session = (
      <EmptyState
        className="border-solid border-success/20 bg-success/10"
        icon={<Sparkles className="h-8 w-8 text-success-strong" aria-hidden />}
        title="Nothing to review today!"
        description="Come back tomorrow. SM-2 has scheduled your next session."
        action={{ label: 'Back to Library', href: '/listening/pronunciation' }}
      />
    );
  } else if (!currentCard) {
    // Session complete → confirmation screen.
    session = (
      <Card padding="lg" className="border-success/20 bg-success/10 py-12 text-center">
        <Trophy className="mx-auto h-12 w-12 text-success-strong" aria-hidden />
        <p className="mt-3 text-2xl font-bold text-navy">All done!</p>
        <p className="mt-1 text-sm text-muted">
          You reviewed {completed} {completed === 1 ? 'card' : 'cards'}. SM-2 has scheduled the next
          round for each one.
        </p>
        <div className="mt-6 flex flex-wrap justify-center gap-3">
          <Button onClick={handleFinish}>Back to Library</Button>
          <Button asChild variant="outline" className="bg-surface">
            <Link href="/listening">Listening Hub</Link>
          </Button>
        </div>
      </Card>
    );
  } else {
    // ── Flashcard view ───────────────────────────────────────────────────────
    session = (
      <div className="space-y-6">
        {/* Progress bar: the shared ProgressBar slides with a transform, not width. */}
        <div className="space-y-1">
          <div className="flex justify-between text-xs font-medium tabular-nums text-muted">
            <span>
              {completed} of {total} reviewed
            </span>
            <span>{Math.round(progressPct)}%</span>
          </div>
          <ProgressBar value={progressPct} ariaLabel={`${completed} of ${total} reviewed`} />
        </div>

        {/* Flashcard */}
        <Card padding="lg" className="py-8">
          {/* Audio + Word */}
          <div className="flex flex-col items-center gap-4">
            {/* An audio control: colour tokens only, no hover scaling. */}
            <button
              type="button"
              disabled={!audioUrl}
              onClick={handlePlay}
              className="flex h-20 w-20 items-center justify-center rounded-full border-2 border-primary/20 bg-lavender text-primary transition-colors hover:border-primary/40 hover:bg-primary/15 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
              aria-label={`Play pronunciation of ${currentCard.word}`}
            >
              <Volume2 className="h-9 w-9" aria-hidden />
            </button>
            {!audioUrl ? (
              <p className="text-xs text-muted">
                Audio asset not yet available
              </p>
            ) : null}

            {revealed ? (
              <div className="w-full text-center">
                <h2 className="break-words text-3xl font-bold text-navy">
                  {currentCard.word}
                </h2>
                {currentCard.pronunciationIpa ? (
                  <p className="mt-1 font-mono text-base text-primary">
                    {currentCard.pronunciationIpa}
                  </p>
                ) : null}
                {currentCard.definitionEn ? (
                  <p className="mt-3 text-sm text-muted">
                    {currentCard.definitionEn}
                  </p>
                ) : null}
              </div>
            ) : (
              <div className="w-full text-center">
                <p className="eyebrow text-muted">
                  Listen, then rate yourself
                </p>
                <p className="mt-2 text-lg font-semibold text-muted" aria-hidden>
                  • • • • •
                </p>
              </div>
            )}
          </div>
        </Card>

        {/* Actions */}
        {!revealed ? (
          <Button fullWidth size="lg" onClick={() => setRevealed(true)}>
            Reveal Word
          </Button>
        ) : (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {QUALITY_BUTTONS.map(({ label, emoji, quality, description, className }) => (
              <button
                key={quality}
                type="button"
                disabled={submitting}
                onClick={() => void handleRate(quality)}
                className={`pressable flex min-h-11 flex-col items-center gap-1 rounded-control border px-3 py-3 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50 ${className}`}
              >
                <span className="text-2xl leading-none" aria-hidden>
                  {emoji}
                </span>
                <span className="mt-1">{label}</span>
                <span className="text-3xs font-normal opacity-70">{description}</span>
              </button>
            ))}
          </div>
        )}

        {/* Footer hint icons (decorative) */}
        <div className="flex justify-center gap-4 text-xs text-muted">
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
      </div>
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow="SM-2 Spaced Repetition"
        icon={Headphones}
        accent="purple"
        title="Pronunciation Review"
        description=""
      />
      <div className="mx-auto w-full max-w-xl">{session}</div>
    </>
  );
}
