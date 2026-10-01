'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Brain, CalendarCheck, RefreshCw, Trophy } from 'lucide-react';
import { toast } from 'sonner';
import { isApiError } from '@/lib/api';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import {
  addVocabWord,
  getVocabDue,
  getVocabStats,
  type VocabItemDto,
  type VocabStatsDto,
} from '@/lib/reading-pathway-api';

export default function VocabHubPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [stats, setStats] = useState<VocabStatsDto | null>(null);
  const [dueItems, setDueItems] = useState<VocabItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [newWord, setNewWord] = useState('');
  const [addingWord, setAddingWord] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) { setLoading(false); return; }

    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        const [s, due] = await Promise.all([getVocabStats(), getVocabDue()]);
        if (cancelled) return;
        setStats(s);
        setDueItems(due);
      } catch {
        // stats are non-blocking
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authLoading, isAuthenticated]);

  async function handleAddWord() {
    const word = newWord.trim();
    if (!word) return;
    setAddingWord(true);
    try {
      await addVocabWord(word, 'manual');
      setNewWord('');
      toast.success(`"${word}" added to your deck.`);
      inputRef.current?.focus();
      const [s, due] = await Promise.all([getVocabStats(), getVocabDue()]);
      setStats(s);
      setDueItems(due);
    } catch (error) {
      if (isApiError(error) && (error.status === 503 || error.code === 'vocabulary_generation_unavailable')) {
        toast.error(`A definition for "${word}" is unavailable right now. No card was stored. Try again later.`);
      } else {
        toast.error('Could not add word. Please try again.');
      }
    } finally {
      setAddingWord(false);
    }
  }

  const statCards = [
    { label: 'Total Words', value: stats?.total ?? '–', icon: BookOpen, accent: 'violet' },
    { label: 'Mastered',    value: stats?.mastered ?? '–', icon: Trophy, accent: 'emerald' },
    { label: 'Due Today',   value: stats?.dueToday ?? '–', icon: CalendarCheck, accent: 'amber' },
    { label: 'Avg Retention', value: stats ? `${Math.round(stats.averageRetention)}%` : '–', icon: Brain, accent: 'blue' },
  ] as const;

  const accentMap: Record<string, string> = {
    violet:  'bg-primary-50 border-primary-200 text-primary-700 dark:bg-primary-950/40 dark:border-primary-800/50 dark:text-primary-300',
    emerald: 'bg-emerald-50 border-emerald-200 text-emerald-700 dark:bg-emerald-950/40 dark:border-emerald-800/50 dark:text-emerald-300',
    amber:   'bg-amber-50 border-amber-200 text-amber-700 dark:bg-amber-950/40 dark:border-amber-800/50 dark:text-amber-300',
    blue:    'bg-blue-50 border-blue-200 text-blue-700 dark:bg-blue-950/40 dark:border-blue-800/50 dark:text-blue-300',
  };

  return (
    <>
      <div className="space-y-6 sm:space-y-10">
        {/* Hero */}
        <LearnerPageHero
          eyebrow="SM-2 Spaced Repetition"
          icon={BookOpen}
          title="Vocabulary Builder"
          description="Build your OET medical vocabulary with evidence-based spaced repetition. Review daily to maximise retention."
        />

        {/* Stats strip */}
        <section>
          {loading ? (
            <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {[0, 1, 2, 3].map((i) => (
                <Skeleton key={i} className="h-24 rounded-xl" />
              ))}
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {statCards.map(({ label, value, icon: Icon, accent }) => (
                <div
                  key={label}
                  className={`flex flex-col gap-2 rounded-xl border px-5 py-4 ${accentMap[accent]}`}
                >
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold uppercase tracking-wide opacity-70">
                      {label}
                    </span>
                    <Icon className="h-4 w-4 opacity-60" aria-hidden />
                  </div>
                  <p className="text-2xl font-bold">{value}</p>
                </div>
              ))}
            </div>
          )}
        </section>

        {/* Quick actions */}
        <section className="flex flex-wrap gap-3">
          <Button asChild size="lg">
            <Link href="/reading/vocab/review">
              <RefreshCw className="h-4 w-4" aria-hidden />
              Review Today&apos;s Cards
              {stats?.dueToday ? (
                <span className="ml-1 rounded-full bg-white/20 px-2 py-0.5 text-xs">
                  {stats.dueToday}
                </span>
              ) : null}
            </Link>
          </Button>
          <Button asChild size="lg" variant="outline" className="bg-surface">
            <Link href="/reading/vocab/lists">
              <BookOpen className="h-4 w-4" aria-hidden />
              Browse Lists
            </Link>
          </Button>
          <Button asChild size="lg" variant="outline" className="bg-surface">
            <Link href="/reading/vocab/stats">
              <Brain className="h-4 w-4" aria-hidden />
              View Stats
            </Link>
          </Button>
        </section>

        {/* Add a word */}
        <section className="rounded-2xl border border-border bg-surface px-4 py-5 shadow-sm sm:px-6 sm:py-6">
          <h2 id="vocab-add-word-title" className="mb-3 text-base font-semibold text-navy">
            Add a Word
          </h2>
          <div className="flex gap-3">
            <input
              ref={inputRef}
              type="text"
              aria-labelledby="vocab-add-word-title"
              value={newWord}
              onChange={(e) => setNewWord(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void handleAddWord(); }}
              placeholder="e.g. haemoglobin"
              className="min-h-11 min-w-0 flex-1 rounded-xl border border-border bg-background-light px-4 py-2.5 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
            <Button
              disabled={addingWord || !newWord.trim()}
              onClick={() => void handleAddWord()}
            >
              {addingWord ? 'Adding…' : 'Add'}
            </Button>
          </div>
        </section>

        {/* Words due today preview */}
        {dueItems.length > 0 ? (
          <section>
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-base font-semibold text-navy">
                Words Due Today
              </h2>
              <Link
                href="/reading/vocab/review"
                className="text-sm font-medium text-primary-600 hover:underline dark:text-primary-400"
              >
                Review All →
              </Link>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3">
              {dueItems.slice(0, 5).map((item) => (
                <div
                  key={item.id}
                  className="rounded-xl border border-primary-100 bg-primary-50/60 px-4 py-3 dark:border-primary-900/40 dark:bg-primary-950/20"
                >
                  <p className="font-semibold text-navy">{item.word}</p>
                  <p className="mt-0.5 line-clamp-2 text-xs text-muted">
                    {item.definitionEn}
                  </p>
                </div>
              ))}
            </div>
          </section>
        ) : null}
      </div>
    </>
  );
}
