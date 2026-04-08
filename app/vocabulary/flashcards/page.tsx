'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Layers, CheckCircle2, RotateCcw, ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchDueFlashcards, submitFlashcardReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type Flashcard = {
  id: string;
  termId: string;
  word: string;
  definition: string;
  exampleSentence: string | null;
  pronunciation: string | null;
  mastery: string;
};

const QUALITY_OPTIONS = [
  { q: 0, label: 'Forgot', color: 'bg-red-500 hover:bg-red-600 text-white' },
  { q: 2, label: 'Hard', color: 'bg-orange-500 hover:bg-orange-600 text-white' },
  { q: 3, label: 'Good', color: 'bg-blue-500 hover:bg-blue-600 text-white' },
  { q: 5, label: 'Easy', color: 'bg-green-500 hover:bg-green-600 text-white' },
];

export default function FlashcardsPage() {
  const [cards, setCards] = useState<Flashcard[]>([]);
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
      const loadedCards = Array.isArray(data) ? data : (data as { cards?: Flashcard[] }).cards ?? [];
      setCards(loadedCards as Flashcard[]);
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

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/vocabulary" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero
          title="Flashcard Review"
          description={`${cards.length} cards due for review`}
          icon={Layers}
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {loading ? (
        <Skeleton className="h-64 rounded-2xl" />
      ) : done ? (
        <MotionSection className="max-w-md mx-auto text-center py-16">
          <CheckCircle2 className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">All done!</h2>
          <p className="text-gray-500 mb-6">{stats.reviewed} cards reviewed · {stats.easy} marked easy</p>
          <div className="flex gap-3 justify-center">
            <Link href="/vocabulary" className="px-5 py-2.5 border border-gray-300 dark:border-gray-600 rounded-xl text-sm font-medium text-gray-700 dark:text-gray-300">
              Back to Vocabulary
            </Link>
            <button onClick={() => { setCurrent(0); setFlipped(false); setDone(false); setStats({ reviewed: 0, easy: 0 }); }} className="px-5 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-sm font-medium flex items-center gap-1.5">
              <RotateCcw className="w-4 h-4" /> Review Again
            </button>
          </div>
        </MotionSection>
      ) : cards.length === 0 ? (
        <div className="text-center py-16">
          <CheckCircle2 className="w-12 h-12 text-green-400 mx-auto mb-3" />
          <p className="text-gray-500">No flashcards due right now. Come back later!</p>
          <Link href="/vocabulary" className="mt-4 inline-block text-sm text-indigo-600 hover:underline">Back to Vocabulary</Link>
        </div>
      ) : card ? (
        <div className="max-w-xl mx-auto">
          {/* Progress bar */}
          <div className="flex items-center justify-between mb-3 text-sm text-gray-500">
            <span>{current + 1} / {cards.length}</span>
          </div>
          <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-1.5 mb-6">
            <div className="bg-indigo-500 h-1.5 rounded-full transition-all" style={{ width: `${Math.min(100, ((current + 1) / cards.length) * 100)}%` }} />
          </div>

          <AnimatePresence mode="wait">
            <motion.div
              key={card.id + (flipped ? '-back' : '-front')}
              initial={{ rotateY: flipped ? -90 : 90, opacity: 0 }}
              animate={{ rotateY: 0, opacity: 1 }}
              exit={{ rotateY: flipped ? 90 : -90, opacity: 0 }}
              transition={{ duration: 0.2 }}
              className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-8 min-h-[220px] flex flex-col items-center justify-center text-center cursor-pointer select-none mb-4"
              onClick={() => !flipped && setFlipped(true)}
            >
              {!flipped ? (
                <>
                  <div className="text-xs font-medium text-indigo-400 uppercase mb-4">WORD</div>
                  <div className="text-3xl font-bold text-gray-900 dark:text-white mb-2">{card.word}</div>
                  {card.pronunciation && <div className="text-gray-400 text-sm italic">{card.pronunciation}</div>}
                  <div className="mt-6 text-xs text-gray-400">Tap to reveal definition</div>
                </>
              ) : (
                <>
                  <div className="text-xs font-medium text-green-400 uppercase mb-4">DEFINITION</div>
                  <div className="text-lg text-gray-900 dark:text-white mb-4">{card.definition}</div>
                  {card.exampleSentence && (
                    <div className="text-sm text-gray-500 italic border-t border-gray-100 dark:border-gray-700 pt-3 mt-2 w-full">
                      &quot;{card.exampleSentence}&quot;
                    </div>
                  )}
                </>
              )}
            </motion.div>
          </AnimatePresence>

          {flipped && (
            <MotionSection className="grid grid-cols-4 gap-2">
              {QUALITY_OPTIONS.map(opt => (
                <button
                  key={opt.q}
                  onClick={() => handleRate(opt.q)}
                  disabled={submitting}
                  className={`py-3 rounded-xl font-medium text-sm transition-colors ${opt.color} disabled:opacity-50`}
                >
                  {opt.label}
                </button>
              ))}
            </MotionSection>
          )}
        </div>
      ) : null}
    </LearnerDashboardShell>
  );
}
