'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { BookOpen, Layers, HelpCircle, Plus } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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
    { href: '/vocabulary/flashcards', label: 'Flashcard Review', icon: <Layers className="w-6 h-6" />, badge: dueCount > 0 ? `${dueCount} due` : null, color: 'bg-indigo-50 dark:bg-indigo-900/20 border-indigo-200 dark:border-indigo-800 text-indigo-700 dark:text-indigo-300' },
    { href: '/vocabulary/quiz', label: 'Vocabulary Quiz', icon: <HelpCircle className="w-6 h-6" />, badge: null, color: 'bg-green-50 dark:bg-green-900/20 border-green-200 dark:border-green-800 text-green-700 dark:text-green-300' },
    { href: '/vocabulary/browse', label: 'Browse Terms', icon: <BookOpen className="w-6 h-6" />, badge: null, color: 'bg-blue-50 dark:bg-blue-900/20 border-blue-200 dark:border-blue-800 text-blue-700 dark:text-blue-300' },
  ];

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Vocabulary"
        description="Build your medical English vocabulary with spaced repetition"
        icon={BookOpen}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Quick links */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-8">
        {quickLinks.map(link => (
          <Link key={link.href} href={link.href}>
            <motion.div
              whileHover={{ scale: 1.02 }}
              className={`flex items-center gap-3 p-4 rounded-xl border cursor-pointer ${link.color}`}
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
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      ) : (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
          {[
            { label: 'Total Words', value: stats.total, color: 'text-gray-800 dark:text-white' },
            { label: 'Mastered', value: stats.mastered, color: 'text-green-600 dark:text-green-400' },
            { label: 'Learning', value: stats.learning, color: 'text-blue-600 dark:text-blue-400' },
            { label: 'New', value: stats.new, color: 'text-gray-500' },
          ].map(s => (
            <div key={s.label} className="bg-white dark:bg-gray-800 rounded-xl p-4 border border-gray-200 dark:border-gray-700 text-center">
              <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
              <div className="text-xs text-gray-500 mt-1">{s.label}</div>
            </div>
          ))}
        </div>
      )}

      {/* My list */}
      <LearnerSurfaceSectionHeader title="My Word List" />
      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-xl" />)}
        </div>
      ) : myList.length === 0 ? (
        <div className="text-center py-12">
          <BookOpen className="w-10 h-10 text-gray-300 mx-auto mb-3" />
          <p className="text-gray-500">Your vocabulary list is empty.</p>
          <Link href="/vocabulary/browse" className="mt-3 inline-flex items-center gap-1 text-sm text-indigo-600 hover:underline">
            <Plus className="w-4 h-4" /> Browse terms to add
          </Link>
        </div>
      ) : (
        <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 overflow-hidden">
          {myList.slice(0, 20).map((item, i) => (
            <motion.div
              key={item.termId}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ delay: i * 0.02 }}
              className="flex items-center gap-3 px-4 py-3 border-b border-gray-100 dark:border-gray-700 last:border-0"
            >
              <div className="flex-1 font-medium text-gray-900 dark:text-white text-sm">{item.word}</div>
              <span className={`px-2 py-0.5 rounded-full text-xs font-medium capitalize ${MASTERY_COLORS[item.mastery] ?? ''}`}>
                {item.mastery}
              </span>
            </motion.div>
          ))}
          {myList.length > 20 && (
            <div className="px-4 py-3 text-center text-sm text-gray-500">
              +{myList.length - 20} more words in your list
            </div>
          )}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
