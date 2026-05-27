'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  Brain,
  CalendarCheck,
  Headphones,
  RefreshCw,
  Trash2,
  Trophy,
  Volume2,
} from 'lucide-react';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
import { useAuth } from '@/contexts/auth-context';
import {
  addPronunciationCard,
  getPronunciationCards,
  getPronunciationStats,
  removePronunciationCard,
  type PronunciationCardDto,
  type PronunciationStatsDto,
} from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Pronunciation library hub — Phase 4 of OET_LISTENING_MODULE_PATHWAY §15.
//
// Mirrors the Reading vocab hub at app/reading/vocab/page.tsx: header KPIs,
// quick-action CTAs, "Add a word" form, and a grid of every card the learner
// has subscribed to (with SM-2 mastery progress bar + next-review date).
//
// Audio playback is wired through the master row's AudioBritishUrl /
// AudioAustralianUrl. When the audio asset is missing (typical for a fresh
// stub card) the speaker button is rendered disabled.
// ─────────────────────────────────────────────────────────────────────────────

const STAT_ACCENTS: Record<string, string> = {
  violet:
    'bg-violet-50 border-violet-200 text-violet-700 dark:bg-violet-950/40 dark:border-violet-800/50 dark:text-violet-300',
  emerald:
    'bg-emerald-50 border-emerald-200 text-emerald-700 dark:bg-emerald-950/40 dark:border-emerald-800/50 dark:text-emerald-300',
  amber:
    'bg-amber-50 border-amber-200 text-amber-700 dark:bg-amber-950/40 dark:border-amber-800/50 dark:text-amber-300',
  rose: 'bg-rose-50 border-rose-200 text-rose-700 dark:bg-rose-950/40 dark:border-rose-800/50 dark:text-rose-300',
};

function formatNextReview(iso: string | null): string {
  if (!iso) return 'soon';
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return 'soon';
  const now = new Date();
  const diffDays = Math.round((at.getTime() - now.getTime()) / 86_400_000);
  if (diffDays < 0) return 'due now';
  if (diffDays === 0) return 'today';
  if (diffDays === 1) return 'tomorrow';
  if (diffDays < 7) return `in ${diffDays} days`;
  if (diffDays < 30) return `in ${Math.round(diffDays / 7)} weeks`;
  return `in ${Math.round(diffDays / 30)} months`;
}

function masteryPercent(card: PronunciationCardDto): number {
  // Combine SM-2 repetitions (0..4 ⇒ 0..80%) with the additive retention
  // score (0..100 ⇒ 0..20% boost) so a card with many reps + high retention
  // appears closer to fully mastered.
  const repsContribution = Math.min(card.repetitions, 4) * 20;
  const retentionContribution = Math.round(card.retentionScore / 5);
  return Math.min(100, repsContribution + retentionContribution);
}

export default function PronunciationHubPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [stats, setStats] = useState<PronunciationStatsDto | null>(null);
  const [cards, setCards] = useState<PronunciationCardDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [newWord, setNewWord] = useState('');
  const [adding, setAdding] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  async function refresh() {
    const [s, list] = await Promise.all([
      getPronunciationStats().catch(() => null),
      getPronunciationCards().catch(() => [] as PronunciationCardDto[]),
    ]);
    setStats(s);
    setCards(list);
  }

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        await refresh();
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  async function handleAdd() {
    const word = newWord.trim();
    if (!word) return;
    setAdding(true);
    try {
      await addPronunciationCard(word, 'manual');
      setNewWord('');
      toast.success(`"${word}" added to your pronunciation deck.`);
      inputRef.current?.focus();
      await refresh();
    } catch {
      toast.error('Could not add word. Please try again.');
    } finally {
      setAdding(false);
    }
  }

  async function handleRemove(card: PronunciationCardDto) {
    try {
      await removePronunciationCard(card.id);
      toast.success(`Removed "${card.word}" from your deck.`);
      await refresh();
    } catch {
      toast.error('Could not remove this card.');
    }
  }

  function handlePlay(url: string | null) {
    if (!url) return;
    if (!audioRef.current) audioRef.current = new Audio();
    audioRef.current.src = url;
    audioRef.current.play().catch(() => {
      toast.error('Audio playback failed.');
    });
  }

  const statCards = [
    { label: 'Total Cards', value: stats?.total ?? '—', icon: BookOpen, accent: 'violet' as const },
    { label: 'Mastered', value: stats?.mastered ?? '—', icon: Trophy, accent: 'emerald' as const },
    { label: 'Due Today', value: stats?.dueToday ?? '—', icon: CalendarCheck, accent: 'amber' as const },
    { label: 'Struggling', value: stats?.struggling ?? '—', icon: Brain, accent: 'rose' as const },
  ];

  return (
    <LearnerDashboardShell pageTitle="Pronunciation Library">
      <main className="space-y-10">
        {/* Hero */}
        <div className="rounded-2xl border border-violet-200 bg-violet-50 px-8 py-7 dark:border-violet-900/50 dark:bg-violet-950/30">
          <p className="mb-1 text-xs font-semibold uppercase tracking-widest text-violet-500">
            SM-2 Spaced Repetition
          </p>
          <h1 className="flex items-center gap-3 text-2xl font-bold text-neutral-900 dark:text-white">
            <Headphones className="h-6 w-6 text-violet-500" aria-hidden />
            Pronunciation Library
          </h1>
          <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
            Train your ear on healthcare vocabulary. Listen, repeat, and let SM-2 schedule the next
            review for maximum retention.
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
                  className={`flex flex-col gap-2 rounded-xl border px-5 py-4 ${STAT_ACCENTS[accent]}`}
                >
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold uppercase tracking-wide opacity-70">{label}</span>
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
            href="/listening/pronunciation/review"
            className="inline-flex items-center gap-2 rounded-xl bg-violet-600 px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-violet-700 active:scale-95"
          >
            <RefreshCw className="h-4 w-4" aria-hidden />
            Review Today&apos;s Cards
            {stats?.dueToday ? (
              <span className="ml-1 rounded-full bg-white/20 px-2 py-0.5 text-xs">{stats.dueToday}</span>
            ) : null}
          </Link>
          <Link
            href="/listening"
            className="inline-flex items-center gap-2 rounded-xl border border-violet-200 bg-white px-5 py-3 text-sm font-semibold text-violet-700 transition hover:bg-violet-50 dark:border-violet-800/60 dark:bg-neutral-900 dark:text-violet-300 dark:hover:bg-violet-950/30"
          >
            <BookOpen className="h-4 w-4" aria-hidden />
            Back to Listening Hub
          </Link>
        </section>

        {/* Add a word */}
        <section className="rounded-2xl border border-neutral-200 bg-white px-6 py-6 dark:border-neutral-800 dark:bg-neutral-900">
          <h2 className="mb-3 text-base font-semibold text-neutral-900 dark:text-white">Add a Word</h2>
          <div className="flex gap-3">
            <input
              ref={inputRef}
              type="text"
              value={newWord}
              onChange={(e) => setNewWord(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleAdd();
              }}
              placeholder="e.g. dyspnoea"
              className="flex-1 rounded-xl border border-neutral-200 bg-neutral-50 px-4 py-2.5 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-violet-400 focus:outline-none focus:ring-2 focus:ring-violet-200 dark:border-neutral-700 dark:bg-neutral-800 dark:text-white dark:placeholder:text-neutral-500"
            />
            <button
              type="button"
              disabled={adding || !newWord.trim()}
              onClick={() => void handleAdd()}
              className="rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-violet-700 disabled:opacity-50"
            >
              {adding ? 'Adding…' : 'Add'}
            </button>
          </div>
          <p className="mt-2 text-xs text-neutral-500 dark:text-neutral-400">
            New cards are due immediately so you can practise them right after adding.
          </p>
        </section>

        {/* Card grid */}
        <section>
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-base font-semibold text-neutral-900 dark:text-white">Your Cards</h2>
            <p className="text-xs text-neutral-500 dark:text-neutral-400">
              {cards.length === 0 ? '' : `${cards.length} total`}
            </p>
          </div>

          {loading ? (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {[0, 1, 2].map((i) => (
                <div
                  key={i}
                  className="h-32 animate-pulse rounded-xl border border-neutral-200 bg-neutral-100 dark:border-neutral-700 dark:bg-neutral-800"
                />
              ))}
            </div>
          ) : cards.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-neutral-300 bg-neutral-50/60 px-8 py-12 text-center dark:border-neutral-700 dark:bg-neutral-900/40">
              <Headphones className="mx-auto h-10 w-10 text-violet-400" aria-hidden />
              <p className="mt-3 text-lg font-semibold text-neutral-900 dark:text-white">
                No pronunciation cards yet
              </p>
              <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
                Add a word above to start training your ear on healthcare vocabulary.
              </p>
              <button
                type="button"
                onClick={() => inputRef.current?.focus()}
                className="mt-5 inline-flex rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-violet-700"
              >
                Add Your First Word
              </button>
            </div>
          ) : (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {cards.map((card) => {
                const mastery = masteryPercent(card);
                const audioUrl = card.audioBritishUrl ?? card.audioAustralianUrl ?? null;
                return (
                  <article
                    key={card.id}
                    className="group rounded-xl border border-neutral-200 bg-white px-4 py-4 transition hover:border-violet-300 hover:shadow-sm dark:border-neutral-700 dark:bg-neutral-900 dark:hover:border-violet-700"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0 flex-1">
                        <h3 className="truncate text-lg font-semibold text-neutral-900 dark:text-white">
                          {card.word}
                        </h3>
                        {card.pronunciationIpa ? (
                          <p className="mt-0.5 truncate font-mono text-xs text-violet-600 dark:text-violet-400">
                            {card.pronunciationIpa}
                          </p>
                        ) : (
                          <p className="mt-0.5 text-xs text-neutral-400 dark:text-neutral-600">
                            IPA pending
                          </p>
                        )}
                      </div>
                      <div className="flex items-center gap-1">
                        <button
                          type="button"
                          disabled={!audioUrl}
                          onClick={() => handlePlay(audioUrl)}
                          className="rounded-lg border border-violet-200 bg-violet-50 p-1.5 text-violet-600 transition hover:bg-violet-100 disabled:opacity-40 dark:border-violet-900/50 dark:bg-violet-950/40 dark:text-violet-400"
                          aria-label={`Play pronunciation of ${card.word}`}
                        >
                          <Volume2 className="h-4 w-4" aria-hidden />
                        </button>
                        <button
                          type="button"
                          onClick={() => void handleRemove(card)}
                          className="rounded-lg border border-neutral-200 bg-white p-1.5 text-neutral-400 opacity-0 transition hover:border-rose-300 hover:bg-rose-50 hover:text-rose-600 group-hover:opacity-100 dark:border-neutral-700 dark:bg-neutral-900 dark:hover:border-rose-800/60 dark:hover:bg-rose-950/30"
                          aria-label={`Remove ${card.word} from deck`}
                        >
                          <Trash2 className="h-4 w-4" aria-hidden />
                        </button>
                      </div>
                    </div>

                    {card.definitionEn ? (
                      <p className="mt-2 line-clamp-2 text-xs text-neutral-500 dark:text-neutral-400">
                        {card.definitionEn}
                      </p>
                    ) : null}

                    {/* Mastery progress */}
                    <div className="mt-3">
                      <div className="mb-1 flex items-center justify-between text-xs">
                        <span className="font-medium text-neutral-500 dark:text-neutral-400">Mastery</span>
                        <span className="font-semibold text-violet-600 dark:text-violet-400">{mastery}%</span>
                      </div>
                      <div className="h-1.5 w-full overflow-hidden rounded-full bg-neutral-200 dark:bg-neutral-700">
                        <div
                          className="h-full rounded-full bg-violet-500 transition-all duration-300"
                          style={{ width: `${mastery}%` }}
                        />
                      </div>
                    </div>

                    <div className="mt-3 flex items-center justify-between text-xs text-neutral-500 dark:text-neutral-400">
                      <span>{card.repetitions} reviews</span>
                      <span>Next: {formatNextReview(card.nextReviewAt)}</span>
                    </div>
                  </article>
                );
              })}
            </div>
          )}
        </section>
      </main>
    </LearnerDashboardShell>
  );
}
