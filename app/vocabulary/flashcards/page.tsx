'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Layers, CheckCircle2, RotateCcw, ArrowLeft, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchDueFlashcards, submitFlashcardReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyFlashcard } from '@/lib/types/vocabulary';

const QUALITY_OPTIONS = [
  { q: 0, key: '1', label: 'Forgot', color: 'bg-danger hover:bg-danger/90 text-white' },
  { q: 2, key: '2', label: 'Hard', color: 'bg-warning hover:bg-warning/90 text-white' },
  { q: 3, key: '3', label: 'Good', color: 'bg-info hover:bg-info/90 text-white' },
  { q: 5, key: '4', label: 'Easy', color: 'bg-success hover:bg-success/90 text-white' },
];

export default function FlashcardsPage() {
  const [cards, setCards] = useState<VocabularyFlashcard[]>([]);
  const [current, setCurrent] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stats, setStats] = useState({ reviewed: 0, easy: 0 });

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

  function playAudio(url: string | null) {
    if (!url) return;
    try { void new Audio(url).play(); } catch {/* ignore */}
  }

  // Keyboard: Space=flip, 1-4=rate (after flip), Arrow=flip/next.
  useEffect(() => {
    function onKey(ev: KeyboardEvent) {
      if (!card || done) return;
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
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero
          title="Flashcard Review"
          description={`${cards.length} cards due for review · Space to flip · 1–4 to rate`}
          icon={Layers}
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {loading ? (
        <Skeleton className="h-64 rounded-2xl" />
      ) : done ? (
        <MotionSection className="mx-auto max-w-md py-16 text-center">
          <Card className="border-border bg-surface p-8">
            <CheckCircle2 className="mx-auto mb-4 h-16 w-16 text-success" />
            <h2 className="mb-2 text-2xl font-bold text-navy">All done!</h2>
            <p className="mb-6 text-muted">{stats.reviewed} cards reviewed · {stats.easy} marked easy</p>
          <div className="flex gap-3 justify-center">
            <Link href="/vocabulary" className="rounded-xl border border-border bg-background-light px-5 py-2.5 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-surface">
              Back to Vocabulary
            </Link>
            <button onClick={() => { setCurrent(0); setFlipped(false); setDone(false); setStats({ reviewed: 0, easy: 0 }); }} className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
              <RotateCcw className="w-4 h-4" /> Review Again
            </button>
          </div>
          </Card>
        </MotionSection>
      ) : cards.length === 0 ? (
        <Card className="border-border bg-surface px-4 sm:px-8 py-8 sm:py-16 text-center">
          <CheckCircle2 className="mx-auto mb-3 h-12 w-12 text-success" />
          <p className="text-muted">No flashcards due right now. Come back later!</p>
          <Link href="/vocabulary" className="mt-4 inline-block text-sm font-medium text-primary hover:underline">Back to Vocabulary</Link>
        </Card>
      ) : card ? (
        <div className="max-w-xl mx-auto">
          <LearnerSurfaceSectionHeader
            eyebrow="Review Session"
            title="Flip the card"
            description="Rate each term to keep your spaced repetition on track."
            className="mb-4"
          />
          <div className="mb-3 flex items-center justify-between text-sm text-muted">
            <span>{current + 1} / {cards.length}</span>
          </div>
          <div className="mb-6 h-1.5 w-full rounded-full bg-background-light">
            <div className="h-1.5 rounded-full bg-primary transition-all" style={{ width: `${Math.min(100, ((current + 1) / cards.length) * 100)}%` }} />
          </div>

          <AnimatePresence mode="wait">
            <motion.div
              key={card.id + (flipped ? '-back' : '-front')}
              initial={{ rotateY: flipped ? -90 : 90, opacity: 0 }}
              animate={{ rotateY: 0, opacity: 1 }}
              exit={{ rotateY: flipped ? 90 : -90, opacity: 0 }}
              transition={{ duration: 0.2 }}
              className="mb-4 flex min-h-[220px] cursor-pointer select-none flex-col items-center justify-center rounded-2xl border border-border bg-surface p-8 text-center"
              onClick={() => !flipped && setFlipped(true)}
              role="button"
              tabIndex={0}
              aria-live="polite"
              aria-label={flipped ? `Definition: ${card.definition}` : `Word: ${card.term}. Press Space to reveal the definition.`}
            >
              {!flipped ? (
                <>
                  <div className="mb-4 text-xs font-medium uppercase text-primary">Word</div>
                  <div className="mb-2 text-3xl font-bold text-navy">{card.term}</div>
                  {card.ipaPronunciation && <div className="text-sm italic text-muted">{card.ipaPronunciation}</div>}
                  {card.audioUrl && (
                    <button
                      onClick={(e) => { e.stopPropagation(); playAudio(card.audioUrl); }}
                      className="mt-3 inline-flex items-center gap-1.5 rounded-full bg-primary/5 px-3 py-1.5 text-xs font-medium text-primary hover:bg-primary/10"
                    >
                      <Volume2 className="h-3.5 w-3.5" /> Play audio
                    </button>
                  )}
                  <div className="mt-6 text-xs text-muted">Tap or press Space to reveal</div>
                </>
              ) : (
                <>
                  <div className="mb-4 text-xs font-medium uppercase text-success">Definition</div>
                  <div className="mb-4 text-lg text-navy">{card.definition}</div>
                  {card.exampleSentence && (
                    <div className="mt-2 w-full border-t border-border pt-3 text-sm italic text-muted">
                      &quot;{card.exampleSentence}&quot;
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
            <MotionSection className="grid grid-cols-2 sm:grid-cols-4 gap-2">
              {QUALITY_OPTIONS.map(opt => (
                <button
                  key={opt.q}
                  onClick={() => handleRate(opt.q)}
                  disabled={submitting}
                  className={`relative rounded-xl py-3 text-sm font-medium transition-colors ${opt.color} disabled:opacity-50`}
                  aria-keyshortcuts={opt.key}
                  aria-label={`${opt.label} (key ${opt.key})`}
                >
                  {opt.label}
                  <span className="absolute top-1 right-2 text-xs opacity-70">{opt.key}</span>
                </button>
              ))}
            </MotionSection>
          )}
        </div>
      ) : null}
    </LearnerDashboardShell>
  );
}
