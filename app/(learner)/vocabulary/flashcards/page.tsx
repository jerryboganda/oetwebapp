'use client';

import { useReducedMotionConfig } from 'motion/react';
import { getFadeSwitchTransition, prefersReducedMotion } from '@/lib/motion';
import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Layers, CheckCircle2, RotateCcw, ArrowLeft, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { RecallTierBadge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { ProgressBar } from '@/components/ui/progress';
import { fetchDueFlashcards, fetchRecallsAudio, submitFlashcardReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import { isEditableEventTarget } from '@/lib/is-editable-target';
import { cleanExampleSentence } from '@/lib/vocabulary-example-sentence';
import type { VocabularyFlashcard } from '@/lib/types/vocabulary';

// Status tints with AA (-strong) text: white on the base success/warning
// fills fails contrast.
const QUALITY_OPTIONS = [
  { q: 0, key: '1', label: 'Forgot', color: 'border-danger/30 bg-danger/10 text-danger-strong hover:bg-danger/15' },
  { q: 2, key: '2', label: 'Hard', color: 'border-warning/30 bg-warning/10 text-warning-strong hover:bg-warning/15' },
  { q: 3, key: '3', label: 'Good', color: 'border-info/30 bg-info/10 text-info hover:bg-info/15' },
  { q: 5, key: '4', label: 'Easy', color: 'border-success/30 bg-success/10 text-success-strong hover:bg-success/15' },
];

export default function FlashcardsPage() {
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const flipTransition = getFadeSwitchTransition(reducedMotion);
  const [cards, setCards] = useState<VocabularyFlashcard[]>([]);
  const [current, setCurrent] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stats, setStats] = useState({ reviewed: 0, easy: 0 });
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  useEffect(() => {
    analytics.track('flashcards_viewed');
    fetchDueFlashcards(20).then(data => {
      const loadedCards = Array.isArray(data) ? data : (data as { cards?: VocabularyFlashcard[] }).cards ?? [];
      setCards(loadedCards as VocabularyFlashcard[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load flashcards.');
      setLoading(false);
    });
  }, []);

  const card = cards[current];

  // §3A — the flashcard shows an example only when it genuinely demonstrates the
  // word; template/filler copy is dropped by the shared guard.
  const cardExampleText = cleanExampleSentence(card?.term, card?.exampleSentence);

  async function handleRate(quality: number) {
    if (!card || submitting) return;
    setSubmitting(true);
    try {
      await submitFlashcardReview(card.id, quality);
      analytics.track('flashcard_rated', { quality, termId: card.termId });
      setStats(s => ({ reviewed: s.reviewed + 1, easy: s.easy + (quality >= 4 ? 1 : 0) }));
      if (current + 1 >= cards.length) {
        setDone(true);
      } else {
        setCurrent(c => c + 1);
        setFlipped(false);
      }
    } catch {
      setError('Failed to submit rating.');
    } finally {
      setSubmitting(false);
    }
  }

  async function playAudio(termId: string) {
    try {
      const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
      if (response) {
        playTransientAudio(response.url);
      }
    } catch {
      setError('Pronunciation audio is not ready yet.');
    }
  }

  // Keyboard: Space=flip, 1-4=rate (after flip), Arrow=flip/next.
  useEffect(() => {
    function onKey(ev: KeyboardEvent) {
      if (!card || done) return;
      // A window-level shortcut must never eat a keystroke aimed at a field.
      if (isEditableEventTarget(ev.target)) return;
      if (ev.key === ' ' || ev.key === 'Enter') {
        ev.preventDefault();
        if (!flipped) setFlipped(true);
        return;
      }
      if (flipped) {
        const n = parseInt(ev.key, 10);
        if (n >= 1 && n <= 4) {
          ev.preventDefault();
          void handleRate(QUALITY_OPTIONS[n - 1].q);
        }
      }
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [card, flipped, done]);

  return (
    <>
      <LearnerPageHero
        title="Flashcard Review"
        description={`${cards.length} cards due for review · Space to flip · 1–4 to rate`}
        icon={Layers}
        aside={(
          <Button variant="ghost" size="sm" asChild>
            <Link href="/vocabulary">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to vocabulary
            </Link>
          </Button>
        )}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}
      {audioUpgradeModal}

      {loading ? (
        <Skeleton className="mx-auto h-64 w-full max-w-xl rounded-2xl" />
      ) : done ? (
        <MotionSection>
          <Card padding="lg" className="flex flex-col items-center text-center">
            <CheckCircle2 className="mb-4 h-16 w-16 text-success-strong" aria-hidden="true" />
            <h2 className="mb-2 text-2xl font-bold text-navy">All done!</h2>
            <p className="mb-6 tabular-nums text-muted">
              <CountUp value={stats.reviewed} /> cards reviewed · {stats.easy} marked easy
            </p>
            <div className="flex flex-wrap justify-center gap-3">
              <Button variant="outline" asChild>
                <Link href="/vocabulary">Back to Vocabulary</Link>
              </Button>
              <Button onClick={() => { setCurrent(0); setFlipped(false); setDone(false); setStats({ reviewed: 0, easy: 0 }); }}>
                <RotateCcw className="w-4 h-4" aria-hidden="true" /> Review Again
              </Button>
            </div>
          </Card>
        </MotionSection>
      ) : cards.length === 0 ? (
        <EmptyState
          icon={<CheckCircle2 className="h-7 w-7 text-success-strong" aria-hidden="true" />}
          title="No flashcards due right now. Come back later!"
          action={{ label: 'Back to Vocabulary', href: '/vocabulary' }}
        />
      ) : card ? (
        // A single focused card: the deck keeps a reading-width column.
        <MotionSection className="mx-auto w-full max-w-xl space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="Review Session"
            title="Flip the card"
            description="Rate each term to keep your spaced repetition on track."
          />
          <div className="space-y-2">
            <div className="text-sm tabular-nums text-muted">{current + 1} / {cards.length}</div>
            <ProgressBar value={current + 1} max={cards.length} ariaLabel={`${current + 1} / ${cards.length}`} />
          </div>

          <AnimatePresence mode="wait">
            <motion.div
              key={card.id + (flipped ? '-back' : '-front')}
              initial={reducedMotion ? { opacity: 0 } : { rotateY: flipped ? -90 : 90, opacity: 0 }}
              animate={reducedMotion ? { opacity: 1 } : { rotateY: 0, opacity: 1 }}
              exit={reducedMotion ? { opacity: 0 } : { rotateY: flipped ? 90 : -90, opacity: 0 }}
              transition={flipTransition}
              className="flex min-h-56 cursor-pointer select-none flex-col items-center justify-center rounded-2xl border border-border bg-surface p-6 text-center shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 sm:p-8"
              onClick={() => !flipped && setFlipped(true)}
              role="button"
              tabIndex={0}
              aria-live="polite"
              aria-label={flipped ? `Definition: ${card.definition}` : `Word: ${card.term}. Press Space to reveal the definition.`}
            >
              {!flipped ? (
                <>
                  <div className="mb-4 eyebrow text-primary">Word</div>
                  <div className="mb-2 flex flex-wrap items-center justify-center gap-2">
                    <span className="text-3xl font-bold text-navy">{card.term}</span>
                    <RecallTierBadge count={card.examFrequencyCount ?? 0} occurrences={card.recallSetOccurrences} />
                  </div>
                  {card.ipaPronunciation && <div className="text-sm italic text-muted">{card.ipaPronunciation}</div>}
                  <button
                    type="button"
                    onClick={(event) => { event.stopPropagation(); void playAudio(card.termId); }}
                    className="pressable mt-3 inline-flex min-h-11 items-center gap-1.5 rounded-full bg-primary/5 px-3 py-1.5 text-xs font-semibold text-primary hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:min-h-9"
                  >
                    <Volume2 className="h-3.5 w-3.5" aria-hidden="true" /> Play audio
                  </button>
                  <div className="mt-6 text-xs text-muted">Tap or press Space to reveal</div>
                </>
              ) : (
                <>
                  <div className="mb-4 eyebrow text-success-strong">Definition</div>
                  <div className="mb-4 text-lg text-navy">{card.definition}</div>
                  {cardExampleText && (
                    <div className="mt-2 w-full border-t border-border pt-3 text-sm italic text-muted">
                      &quot;{cardExampleText}&quot;
                    </div>
                  )}
                  {card.synonyms?.length > 0 && (
                    <div className="mt-3 flex flex-wrap justify-center gap-1">
                      {card.synonyms.slice(0, 4).map((s, i) => (
                        <span key={i} className="rounded-full bg-background-light px-2 py-0.5 text-xs text-muted">{s}</span>
                      ))}
                    </div>
                  )}
                </>
              )}
            </motion.div>
          </AnimatePresence>

          {flipped && (
            <MotionSection className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              {QUALITY_OPTIONS.map(opt => (
                <button
                  key={opt.q}
                  type="button"
                  onClick={() => handleRate(opt.q)}
                  disabled={submitting}
                  className={`pressable relative min-h-12 rounded-xl border py-3 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${opt.color} disabled:opacity-50`}
                  aria-keyshortcuts={opt.key}
                  aria-label={`${opt.label} (key ${opt.key})`}
                >
                  {opt.label}
                  <span className="absolute end-2 top-1 text-xs tabular-nums opacity-70" aria-hidden="true">{opt.key}</span>
                </button>
              ))}
            </MotionSection>
          )}
        </MotionSection>
      ) : null}
    </>
  );
}
