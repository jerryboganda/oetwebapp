'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Brain, CalendarCheck, RefreshCw, Trophy } from 'lucide-react';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
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
    } catch {
      toast.error('Could not add word. Please try again.');
    } finally {
      setAddingWord(false);
    }
  }

  const statCards = [
    { label: 'Total Words', value: stats?.total ?? '—', icon: BookOpen, accent: 'violet' },
    { label: 'Mastered',    value: stats?.mastered ?? '—', icon: Trophy, accent: 'emerald' },
    { label: 'Due Today',   value: stats?.dueToday ?? '—', icon: CalendarCheck, accent: 'amber' },
    { label: 'Avg Retention', value: stats ? `${Math.round(stats.averageRetention)}%` : '—', icon: Brain, accent: 'blue' },
  ] as const;

  const accentMap: Record<string, string> = {
    violet:  'bg-violet-50 border-violet-200 text-violet-700 dark:bg-violet-950/40 dark:border-violet-800/50 dark:text-violet-300',
    emerald: 'bg-emerald-50 border-emerald-200 text-emerald-700 dark:bg-emerald-950/40 dark:border-emerald-800/50 dark:text-emerald-300',
    amber:   'bg-amber-50 border-amber-200 text-amber-700 dark:bg-amber-950/40 dark:border-amber-800/50 dark:text-amber-300',
    blue:    'bg-blue-50 border-blue-200 text-blue-700 dark:bg-blue-950/40 dark:border-blue-800/50 dark:text-blue-300',
  };

  return (
    <LearnerDashboardShell pageTitle="Vocabulary">
      <main className="space-y-10">
        {/* Hero */}
        <div className="rounded-2xl border border-violet-200 bg-violet-50 px-8 py-7 dark:border-violet-900/50 dark:bg-violet-950/30">
          <p className="mb-1 text-xs font-semibold uppercase tracking-widest text-violet-500">
            SM-2 Spaced Repetition
          </p>
          <h1 className="text-2xl font-bold text-neutral-900 dark:text-white">
            Vocabulary Builder
          </h1>
          <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
            Build your OET medical vocabulary with evidence-based spaced repetition. Review daily to maximise retention.
          </p>
        </div>

        {/* Stats strip */}
        <section>
          {loading ? (
            <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
              {[0, 1, 2, 3].map((i) => (
                <div
                  key={i}
                  className="h-24 animate-pulse rounded-xl border border-neutral-200 bg-neutral-100 dark:border-neutral-700 dark:bg-neutral-800"
                />
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
          <Link
            href="/reading/vocab/review"
            className="inline-flex items-center gap-2 rounded-xl bg-violet-600 px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-violet-700 active:scale-95"
          >
            <RefreshCw className="h-4 w-4" aria-hidden />
            Review Today&apos;s Cards
            {stats?.dueToday ? (
              <span className="ml-1 rounded-full bg-white/20 px-2 py-0.5 text-xs">
                {stats.dueToday}
              </span>
            ) : null}
          </Link>
          <Link
            href="/reading/vocab/lists"
            className="inline-flex items-center gap-2 rounded-xl border border-violet-200 bg-white px-5 py-3 text-sm font-semibold text-violet-700 transition hover:bg-violet-50 dark:border-violet-800/60 dark:bg-neutral-900 dark:text-violet-300 dark:hover:bg-violet-950/30"
          >
            <BookOpen className="h-4 w-4" aria-hidden />
            Browse Lists
          </Link>
          <Link
            href="/reading/vocab/stats"
            className="inline-flex items-center gap-2 rounded-xl border border-neutral-200 bg-white px-5 py-3 text-sm font-semibold text-neutral-700 transition hover:bg-neutral-50 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-300"
          >
            <Brain className="h-4 w-4" aria-hidden />
            View Stats
          </Link>
        </section>

        {/* Add a word */}
        <section className="rounded-2xl border border-neutral-200 bg-white px-6 py-6 dark:border-neutral-800 dark:bg-neutral-900">
          <h2 className="mb-3 text-base font-semibold text-neutral-900 dark:text-white">
            Add a Word
          </h2>
          <div className="flex gap-3">
            <input
              ref={inputRef}
              type="text"
              value={newWord}
              onChange={(e) => setNewWord(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void handleAddWord(); }}
              placeholder="e.g. haemoglobin"
              className="flex-1 rounded-xl border border-neutral-200 bg-neutral-50 px-4 py-2.5 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-violet-400 focus:outline-none focus:ring-2 focus:ring-violet-200 dark:border-neutral-700 dark:bg-neutral-800 dark:text-white dark:placeholder:text-neutral-500"
            />
            <button
              type="button"
              disabled={addingWord || !newWord.trim()}
              onClick={() => void handleAddWord()}
              className="rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-violet-700 disabled:opacity-50"
            >
              {addingWord ? 'Adding…' : 'Add'}
            </button>
          </div>
        </section>

        {/* Words due today preview */}
        {dueItems.length > 0 ? (
          <section>
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-base font-semibold text-neutral-900 dark:text-white">
                Words Due Today
              </h2>
              <Link
                href="/reading/vocab/review"
                className="text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
              >
                Review All →
              </Link>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3">
              {dueItems.slice(0, 5).map((item) => (
                <div
                  key={item.id}
                  className="rounded-xl border border-violet-100 bg-violet-50/60 px-4 py-3 dark:border-violet-900/40 dark:bg-violet-950/20"
                >
                  <p className="font-semibold text-neutral-900 dark:text-white">{item.word}</p>
                  <p className="mt-0.5 line-clamp-2 text-xs text-neutral-500 dark:text-neutral-400">
                    {item.definitionEn}
                  </p>
                </div>
              ))}
            </div>
          </section>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}