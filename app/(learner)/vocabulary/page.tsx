'use client';

import { useEffect, useState } from 'react';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { BookOpen, Layers, HelpCircle, Trash2, History, Flame, Sparkles } from 'lucide-react';
import Link from 'next/link';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import {
  fetchMyVocabulary,
  fetchVocabularyStats,
  fetchVocabularyDailySet,
  removeFromMyVocabulary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { LearnerVocabulary, VocabularyDailySet, VocabularyStats } from '@/lib/types/vocabulary';

type MyVocabItem = Pick<LearnerVocabulary, 'termId' | 'term' | 'mastery' | 'dueAt'>;

const MASTERY_BADGES: Record<string, BadgeProps['variant']> = {
  new: 'slate',
  learning: 'info',
  reviewing: 'warning',
  mastered: 'success',
};

export default function VocabularyPage() {
  const [myList, setMyList] = useState<MyVocabItem[]>([]);
  const [myListTotal, setMyListTotal] = useState(0);
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
        fetchMyVocabulary(undefined, { page: 1, pageSize: 20 }),
        fetchVocabularyStats(),
        fetchVocabularyDailySet(10),
      ]);
      if (listR.status === 'fulfilled') {
        const items = Array.isArray(listR.value) ? listR.value : listR.value.items;
        setMyList(items as MyVocabItem[]);
        setMyListTotal(Array.isArray(listR.value) ? items.length : listR.value.total);
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
      setMyListTotal(prev => Math.max(0, prev - 1));
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
    { href: '/vocabulary/flashcards', label: 'Flashcard Review', icon: <Layers className="w-6 h-6" aria-hidden="true" />, badge: displayStats.dueToday > 0 ? `${displayStats.dueToday} due` : null, iconTile: 'bg-primary/10 text-primary' },
    { href: '/vocabulary/quiz', label: 'Vocabulary Quiz', icon: <HelpCircle className="w-6 h-6" aria-hidden="true" />, badge: null, iconTile: 'bg-success/10 text-success-strong' },
    { href: '/vocabulary/browse', label: 'Browse Terms', icon: <BookOpen className="w-6 h-6" aria-hidden="true" />, badge: null, iconTile: 'bg-info/10 text-info' },
    { href: '/vocabulary/quiz/history', label: 'Quiz History', icon: <History className="w-6 h-6" aria-hidden="true" />, badge: null, iconTile: 'bg-lavender text-primary-dark' },
  ];

  const heroHighlights = [
    { icon: BookOpen, label: 'List size', value: `${displayStats.total}` },
    { icon: Layers, label: 'Due today', value: `${displayStats.dueToday}` },
    { icon: Flame, label: 'Streak', value: `${displayStats.streakDays}d` },
  ];

  return (
    <>
      <LearnerPageHero
        eyebrow="Vocabulary Builder"
        title="Grow your clinical vocabulary one term at a time"
        description="Spaced repetition surfaces the medical and academic words you are weakest on, exactly when you are about to forget them."
        icon={BookOpen}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Quick access"
          title="Jump into a vocabulary mode"
          description="Flashcards, quiz, browse, and history, all in one place."
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {quickLinks.map((link, i) => (
            <MotionItem key={link.href} delayIndex={i} className="h-full">
              <CardLink href={link.href} className="flex h-full items-center gap-3">
                <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${link.iconTile}`}>
                  {link.icon}
                </div>
                <div className="min-w-0">
                  <div className="text-sm font-bold text-navy">{link.label}</div>
                  {link.badge && <div className="text-xs text-muted">{link.badge}</div>}
                </div>
              </CardLink>
            </MotionItem>
          ))}
        </div>
      </MotionSection>

      {loading && !stats ? (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          {[
            { label: 'Total Words', value: displayStats.total, color: 'text-navy' },
            { label: 'Mastered', value: displayStats.mastered, color: 'text-success-strong' },
            { label: 'Learning', value: displayStats.learning, color: 'text-info' },
            { label: 'New', value: displayStats.new, color: 'text-muted' },
          ].map((s, i) => (
            <MotionItem key={s.label} delayIndex={i}>
              <Card className="h-full text-center">
                <div className={`text-3xl font-bold tabular-nums ${s.color}`}><CountUp value={s.value} /></div>
                <div className="mt-1 tile-label text-muted">{s.label}</div>
              </Card>
            </MotionItem>
          ))}
        </div>
      )}

      {/* Daily set CTA — surfaces due + new cards for today */}
      {dailySet && dailySet.cards.length > 0 && (
        <MotionSection>
          <Card className="border-primary/30 bg-lavender/40">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="min-w-0">
                <p className="mb-1 flex items-center gap-2 eyebrow text-primary">
                  <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
                  Today&apos;s set
                </p>
                <h3 className="text-lg font-bold tabular-nums text-navy">
                  {dailySet.cards.length} cards · {dailySet.dueCount} due · {dailySet.newCount} new
                </h3>
                <p className="text-sm text-muted">
                  A focused spaced-repetition session to keep your momentum.
                </p>
              </div>
              <Button asChild>
                <Link href="/vocabulary/flashcards">
                  <Layers className="h-4 w-4" aria-hidden="true" />
                  Start today&apos;s set
                </Link>
              </Button>
            </div>
          </Card>
        </MotionSection>
      )}

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Word bank"
          title="My Word List"
          description="Saved terms with their current mastery tier. Remove any you no longer want to track."
        />
        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-xl" />)}
          </div>
        ) : myList.length === 0 ? (
          <EmptyState
            icon={<BookOpen className="h-7 w-7" aria-hidden="true" />}
            title="Your vocabulary list is empty."
            action={{ label: 'Browse terms to add', href: '/vocabulary/browse' }}
          />
        ) : (
          <Card padding="none" className="overflow-hidden">
            {myList.slice(0, 20).map((item, i) => (
              <MotionItem
                key={item.termId}
                delayIndex={Math.min(i, 5)}
                className="group flex items-center gap-3 border-b border-border px-4 py-2 last:border-0"
              >
                <Link
                  href={`/vocabulary/terms/${encodeURIComponent(item.termId)}`}
                  className="min-w-0 flex-1 rounded py-1 text-sm font-medium text-navy hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  {item.term}
                </Link>
                <Badge variant={MASTERY_BADGES[item.mastery] ?? 'muted'} className="capitalize">
                  {item.mastery}
                </Badge>
                <button
                  type="button"
                  onClick={() => handleRemove(item.termId)}
                  disabled={removing.has(item.termId)}
                  className="inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-muted transition hover:bg-danger/10 hover:text-danger-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50 pointer-fine:opacity-0 pointer-fine:group-hover:opacity-100 focus-visible:opacity-100"
                  aria-label={`Remove ${item.term} from my word list`}
                  title="Remove from my list"
                >
                  <Trash2 className="h-4 w-4" aria-hidden="true" />
                </button>
              </MotionItem>
            ))}
            {myListTotal > myList.length && (
              <div className="px-4 py-3 text-center text-sm tabular-nums text-muted">
                +{myListTotal - myList.length} more words in your list
              </div>
            )}
          </Card>
        )}
      </MotionSection>
    </>
  );
}
