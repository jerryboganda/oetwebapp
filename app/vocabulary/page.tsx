'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { BookOpen, Layers, HelpCircle, Plus } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { fetchMyVocabulary, fetchDueFlashcards } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type MyVocabItem = { termId: string; word: string; mastery: string; dueAt: string | null };

const MASTERY_COLORS: Record<string, string> = {
  new: 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-400',
  learning: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  reviewing: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
  mastered: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
};

export default function VocabularyPage() {
  const [myList, setMyList] = useState<MyVocabItem[]>([]);
  const [dueCount, setDueCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('vocabulary_home_viewed');
    Promise.allSettled([fetchMyVocabulary(), fetchDueFlashcards(100)]).then(([listR, dueR]) => {
      if (listR.status === 'fulfilled') {
        const items = Array.isArray(listR.value) ? listR.value : (listR.value?.items ?? []);
        setMyList(items as MyVocabItem[]);
      }
      if (dueR.status === 'fulfilled') {
        const dueItems = Array.isArray(dueR.value) ? dueR.value : (dueR.value?.cards ?? dueR.value?.items ?? []);
        setDueCount(dueItems.length);
      }
      if (listR.status === 'rejected') setError('Could not load vocabulary list.');
      setLoading(false);
    });
  }, []);

  const stats = {
    total: myList.length,
    mastered: myList.filter(i => i.mastery === 'mastered').length,
    learning: myList.filter(i => i.mastery === 'learning' || i.mastery === 'reviewing').length,
    new: myList.filter(i => i.mastery === 'new').length,
  };

  const quickLinks = [
    { href: '/vocabulary/flashcards', label: 'Flashcard Review', icon: <Layers className="w-6 h-6" />, badge: dueCount > 0 ? `${dueCount} due` : null, color: 'bg-indigo-100 dark:bg-indigo-900/40 border-indigo-300 dark:border-indigo-700 text-indigo-900 dark:text-indigo-200' },
    { href: '/vocabulary/quiz', label: 'Vocabulary Quiz', icon: <HelpCircle className="w-6 h-6" />, badge: null, color: 'bg-emerald-100 dark:bg-emerald-900/40 border-emerald-300 dark:border-emerald-700 text-emerald-900 dark:text-emerald-200' },
    { href: '/vocabulary/browse', label: 'Browse Terms', icon: <BookOpen className="w-6 h-6" />, badge: null, color: 'bg-sky-100 dark:bg-sky-900/40 border-sky-300 dark:border-sky-700 text-sky-900 dark:text-sky-200' },
  ];

  const heroHighlights = [
    { icon: BookOpen, label: 'List size', value: `${myList.length}` },
    { icon: Layers, label: 'Due cards', value: `${dueCount}` },
    { icon: HelpCircle, label: 'Mode', value: 'Spaced repetition' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="mx-auto max-w-6xl space-y-6 px-4 py-6">
      <LearnerPageHero
        eyebrow="Learn"
        title="Vocabulary"
        description="Build your medical English vocabulary with spaced repetition."
        icon={BookOpen}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Quick links */}
      <LearnerSurfaceSectionHeader
        eyebrow="Quick access"
        title="Jump into a vocabulary mode"
        description="The quick links should look like part of the dashboard, not a separate card system."
        className="mb-4"
      />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        {quickLinks.map(link => (
          <Link key={link.href} href={link.href}>
            <motion.div
              whileHover={{ scale: 1.02 }}
              className={`flex cursor-pointer items-center gap-3 rounded-3xl border p-4 shadow-sm ${link.color}`}
            >
              {link.icon}
              <div>
                <div className="font-semibold text-sm">{link.label}</div>
                {link.badge && <div className="text-xs opacity-75">{link.badge}</div>}
              </div>
            </motion.div>
          </Link>
        ))}
      </div>

      {/* Stats */}
      {loading ? (
        <div className="mb-8 grid grid-cols-2 gap-4 md:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-3xl" />)}
        </div>
      ) : (
        <div className="mb-8 grid grid-cols-2 gap-4 md:grid-cols-4">
          {[
            { label: 'Total Words', value: stats.total, color: 'text-gray-800 dark:text-white' },
            { label: 'Mastered', value: stats.mastered, color: 'text-green-600 dark:text-green-400' },
            { label: 'Learning', value: stats.learning, color: 'text-blue-600 dark:text-blue-400' },
            { label: 'New', value: stats.new, color: 'text-gray-500' },
          ].map(s => (
            <Card key={s.label} className="rounded-3xl p-4 text-center shadow-sm">
              <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
              <div className="text-xs text-gray-500 mt-1">{s.label}</div>
            </Card>
          ))}
        </div>
      )}

      {/* My list */}
      <LearnerSurfaceSectionHeader
        eyebrow="Word bank"
        title="My Word List"
        description="Your saved terms should feel like a curated dashboard list, not a raw database dump."
      />
      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-3xl" />)}
        </div>
      ) : myList.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <BookOpen className="mx-auto mb-3 h-10 w-10 text-gray-300" />
          <p className="text-muted">Your vocabulary list is empty.</p>
          <Link href="/vocabulary/browse" className="mt-3 inline-flex items-center gap-1 text-sm text-primary hover:underline">
            <Plus className="w-4 h-4" /> Browse terms to add
          </Link>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-3xl border border-border bg-surface shadow-sm">
          {myList.slice(0, 20).map((item, i) => (
            <MotionItem
              key={item.termId}
              delayIndex={i}
              className="flex items-center gap-3 border-b border-border px-4 py-3 last:border-0"
            >
              <div className="flex-1 text-sm font-medium text-navy">{item.word}</div>
              <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${MASTERY_COLORS[item.mastery] ?? ''}`}>
                {item.mastery}
              </span>
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
