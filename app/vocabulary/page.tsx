'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { BookOpen, Layers, HelpCircle, Plus, Trash2, History, Flame, Sparkles } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import {
  fetchMyVocabulary,
  fetchVocabularyStats,
  fetchVocabularyDailySet,
  removeFromMyVocabulary,
} from '@/lib/api';
import { useReducedMotion } from 'motion/react';
import { analytics } from '@/lib/analytics';
import { getMicroHover, prefersReducedMotion } from '@/lib/motion';
import type { LearnerVocabulary, VocabularyDailySet, VocabularyStats } from '@/lib/types/vocabulary';

type MyVocabItem = Pick<LearnerVocabulary, 'termId' | 'term' | 'mastery' | 'dueAt'>;

const MASTERY_COLORS: Record<string, string> = {
  new: 'bg-background-light text-muted border border-border',
  learning: 'bg-info/10 text-info border border-info/20',
  reviewing: 'bg-warning/10 text-warning border border-warning/20',
  mastered: 'bg-success/10 text-success border border-success/20',
};

export default function VocabularyPage() {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const microHover = getMicroHover(reducedMotion);
  const [myList, setMyList] = useState<MyVocabItem[]>([]);
  const [stats, setStats] = useState<VocabularyStats | null>(null);
  const [dailySet, setDailySet] = useState<VocabularyDailySet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [removing, setRemoving] = useState<Set<string>>(new Set());

  useEffect(() => {
    analytics.track('vocabulary_home_viewed');
    void loadAll();
  }, []);

  async function loadAll() {
    setLoading(true);
    try {
      const [listR, statsR, dailyR] = await Promise.allSettled([
        fetchMyVocabulary(),
        fetchVocabularyStats(),
        fetchVocabularyDailySet(10),
      ]);
      if (listR.status === 'fulfilled') {
        const items = Array.isArray(listR.value) ? listR.value : ((listR.value as { items?: MyVocabItem[] })?.items ?? []);
        setMyList(items as MyVocabItem[]);
      }
      if (statsR.status === 'fulfilled') {
        setStats(statsR.value as VocabularyStats);
      }
      if (dailyR.status === 'fulfilled') {
        setDailySet(dailyR.value as VocabularyDailySet);
      }
      if (listR.status === 'rejected') setError('Could not load your word bank.');
    } finally {
      setLoading(false);
    }
  }

  async function handleRemove(termId: string) {
    if (removing.has(termId)) return;
    setRemoving(prev => new Set(prev).add(termId));
    try {
      await removeFromMyVocabulary(termId);
      analytics.track('vocab_removed', { termId });
      setMyList(prev => prev.filter(i => i.termId !== termId));
      // Refresh stats in the background; keep optimistic UI.
      void fetchVocabularyStats().then(s => setStats(s as VocabularyStats)).catch(() => {});
    } catch {
      setError('Failed to remove term.');
    } finally {
      setRemoving(prev => { const s = new Set(prev); s.delete(termId); return s; });
    }
  }

  const displayStats = {
    total: stats?.totalInList ?? myList.length,
    mastered: stats?.mastered ?? 0,
    learning: (stats?.learning ?? 0) + (stats?.reviewing ?? 0),
    new: stats?.new ?? 0,
    dueToday: stats?.dueToday ?? 0,
    streakDays: stats?.streakDays ?? 0,
  };

  const quickLinks = [
    { href: '/vocabulary/flashcards', label: 'Flashcard Review', icon: <Layers className="w-6 h-6" />, badge: displayStats.dueToday > 0 ? `${displayStats.dueToday} due` : null, iconTile: 'bg-primary/10 text-primary' },
    { href: '/vocabulary/quiz', label: 'Vocabulary Quiz', icon: <HelpCircle className="w-6 h-6" />, badge: null, iconTile: 'bg-emerald-50 text-emerald-700' },
    { href: '/vocabulary/browse', label: 'Browse Terms', icon: <BookOpen className="w-6 h-6" />, badge: null, iconTile: 'bg-info/10 text-info' },
    { href: '/vocabulary/quiz/history', label: 'Quiz History', icon: <History className="w-6 h-6" />, badge: null, iconTile: 'bg-purple-50 text-purple-700' },
  ];

  const heroHighlights = [
    { icon: BookOpen, label: 'List size', value: `${displayStats.total}` },
    { icon: Layers, label: 'Due today', value: `${displayStats.dueToday}` },
    { icon: Flame, label: 'Streak', value: `${displayStats.streakDays}d` },
    { icon: HelpCircle, label: 'Mode', value: 'Spaced repetition' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
      <LearnerPageHero
        eyebrow="Vocabulary Builder"
        title="Grow your clinical vocabulary one term at a time"
        description="Spaced repetition surfaces the medical and academic words you are weakest on, exactly when you are about to forget them."
        icon={BookOpen}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Quick links */}
      <LearnerSurfaceSectionHeader
        eyebrow="Quick access"
        title="Jump into a vocabulary mode"
        description="Flashcards, quiz, browse, and history — all in one place."
        className="mb-4"
      />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {quickLinks.map(link => (
          <Link key={link.href} href={link.href}>
            <motion.div
              whileHover={microHover}
              className="flex cursor-pointer items-center gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm transition-all hover:border-border-hover hover:shadow-md"
            >
              <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${link.iconTile}`}>
                {link.icon}
              </div>
              <div>
                <div className="font-semibold text-sm text-navy">{link.label}</div>
                {link.badge && <div className="text-xs text-muted">{link.badge}</div>}
              </div>
            </motion.div>
          </Link>
        ))}
      </div>

      {/* Stats */}
      {loading && !stats ? (
        <div className="mb-8 grid grid-cols-2 gap-4 md:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-[24px]" />)}
        </div>
      ) : (
        <div className="mb-8 grid grid-cols-2 gap-4 md:grid-cols-4">
          {[
            { label: 'Total Words', value: displayStats.total, color: 'text-navy' },
            { label: 'Mastered', value: displayStats.mastered, color: 'text-green-600 dark:text-green-400' },
            { label: 'Learning', value: displayStats.learning, color: 'text-blue-600 dark:text-blue-400' },
            { label: 'New', value: displayStats.new, color: 'text-muted' },
          ].map(s => (
            <Card key={s.label} className="rounded-[24px] p-4 text-center shadow-sm">
              <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
              <div className="text-xs text-muted mt-1">{s.label}</div>
            </Card>
          ))}
        </div>
      )}

      {/* Daily set CTA — surfaces due + new cards for today */}
      {dailySet && dailySet.cards.length > 0 && (
        <Card className="rounded-[24px] border-primary/30 bg-lavender/40 p-5 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="min-w-0">
              <div className="mb-1 flex items-center gap-2 text-xs font-semibold uppercase text-primary">
                <Sparkles className="h-3.5 w-3.5" />
                Today&apos;s set
              </div>
              <h3 className="text-lg font-bold text-navy">
                {dailySet.cards.length} cards · {dailySet.dueCount} due · {dailySet.newCount} new
              </h3>
              <p className="text-sm text-muted">
                A focused spaced-repetition session to keep your momentum.
              </p>
            </div>
            <Link
              href="/vocabulary/flashcards"
              className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90"
            >
              <Layers className="h-4 w-4" />
              Start today&apos;s set
            </Link>
          </div>
        </Card>
      )}

      {/* My list */}
      <LearnerSurfaceSectionHeader
        eyebrow="Word bank"
        title="My Word List"
        description="Saved terms with their current mastery tier. Remove any you no longer want to track."
      />
      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-[24px]" />)}
        </div>
      ) : myList.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <BookOpen className="mx-auto mb-3 h-10 w-10 text-muted/40" />
          <p className="text-muted">Your vocabulary list is empty.</p>
          <Link href="/vocabulary/browse" className="mt-3 inline-flex items-center gap-1 text-sm text-primary hover:underline">
            <Plus className="w-4 h-4" /> Browse terms to add
          </Link>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-[24px] border border-border bg-surface shadow-sm">
          {myList.slice(0, 20).map((item, i) => (
            <MotionItem
              key={item.termId}
              delayIndex={i}
              className="group flex items-center gap-3 border-b border-border px-4 py-3 last:border-0"
            >
              <Link href={`/vocabulary/terms/${encodeURIComponent(item.termId)}`} className="flex-1 text-sm font-medium text-navy hover:underline">
                {item.term}
              </Link>
              <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${MASTERY_COLORS[item.mastery] ?? ''}`}>
                {item.mastery}
              </span>
              <button
                onClick={() => handleRemove(item.termId)}
                disabled={removing.has(item.termId)}
                className="rounded-lg p-1.5 text-muted opacity-0 transition group-hover:opacity-100 hover:bg-danger/10 hover:text-danger disabled:opacity-50 focus:opacity-100"
                aria-label={`Remove ${item.term} from my word list`}
                title="Remove from my list"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </MotionItem>
          ))}
          {myList.length > 20 && (
            <div className="px-4 py-3 text-center text-sm text-muted">
              +{myList.length - 20} more words in your list
            </div>
          )}
        </div>
      )}
      </div>
    </LearnerDashboardShell>
  );
}
