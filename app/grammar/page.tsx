'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { BookMarked, Clock, CheckCircle2, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchGrammarLessons } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type GrammarLesson = { id: string; examTypeCode: string; title: string; description: string | null; level: string; estimatedMinutes: number; status: string; category: string | null };

const DIFFICULTY_COLORS: Record<string, string> = {
  beginner: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  intermediate: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
  advanced: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
};

export default function GrammarPage() {
  const [lessons, setLessons] = useState<GrammarLesson[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [examType, setExamType] = useState('oet');
  const [level, setLevel] = useState('');

  useEffect(() => {
    analytics.track('grammar_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      setLoading(true);
      setError(null);

      try {
        const data = await fetchGrammarLessons({ examTypeCode: examType, level: level || undefined });
        if (cancelled) return;
        setLessons(data as GrammarLesson[]);
      } catch {
        if (!cancelled) {
          setError('Could not load grammar lessons.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [examType, level]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Grammar Lessons"
        description="Strengthen your grammar for OET and other English exams"
        icon={BookMarked}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Filters */}
      <div className="flex flex-wrap gap-3 mb-6">
        <select
          value={examType}
          onChange={e => setExamType(e.target.value)}
          className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300"
        >
          <option value="oet">OET</option>
          <option value="ielts">IELTS</option>
          <option value="pte">PTE</option>
        </select>
        <select
          value={level}
          onChange={e => setLevel(e.target.value)}
          className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300"
        >
          <option value="">All Levels</option>
          <option value="beginner">Beginner</option>
          <option value="intermediate">Intermediate</option>
          <option value="advanced">Advanced</option>
        </select>
      </div>

      {loading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-36 rounded-xl" />)}
        </div>
      ) : lessons.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No grammar lessons available for this selection.</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {lessons.map((lesson, i) => (
            <motion.div
              key={lesson.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: i * 0.04 }}
            >
              <Link href={`/grammar/${lesson.id}`}>
                <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5 hover:border-purple-300 dark:hover:border-purple-600 hover:shadow-sm transition-all cursor-pointer group">
                  <div className="flex items-start justify-between mb-2">
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-gray-900 dark:text-white mb-1 group-hover:text-purple-600 dark:group-hover:text-purple-400 transition-colors">{lesson.title}</div>
                      {lesson.description && <div className="text-sm text-gray-500 line-clamp-2">{lesson.description}</div>}
                    </div>
                    <ChevronRight className="w-5 h-5 text-gray-300 group-hover:text-purple-400 flex-shrink-0 ml-2 transition-colors" />
                  </div>
                  <div className="flex items-center gap-3 mt-3">
                    <span className={`text-xs px-2 py-0.5 rounded-full font-medium capitalize ${DIFFICULTY_COLORS[lesson.level] ?? 'bg-gray-100 text-gray-500'}`}>
                      {lesson.level}
                    </span>
                    {lesson.category && <span className="text-xs text-gray-400 capitalize">{lesson.category}</span>}
                    <div className="flex items-center gap-1 text-xs text-gray-400">
                      <Clock className="w-3.5 h-3.5" />
                      {lesson.estimatedMinutes} min
                    </div>
                  </div>
                </div>
              </Link>
            </motion.div>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
