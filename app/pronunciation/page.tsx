'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Mic, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchPronunciationDrills, fetchMyPronunciationProgress } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type PronunciationDrill = {
  id: string;
  targetPhoneme: string;
  label: string;
  exampleWordsJson: string;
  difficulty: string;
};

type PronProgress = {
  phonemeCode: string;
  averageScore: number;
  attemptCount: number;
  lastPracticedAt: string | null;
};

const DIFFICULTY_COLORS: Record<string, string> = {
  easy: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  medium: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
  hard: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
};

function parseExampleWords(value: string) {
  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed) ? parsed.filter((word): word is string => typeof word === 'string') : [];
  } catch {
    return [];
  }
}

export default function PronunciationPage() {
  const [drills, setDrills] = useState<PronunciationDrill[]>([]);
  const [progressMap, setProgressMap] = useState<Record<string, PronProgress>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [difficulty, setDifficulty] = useState('');

  useEffect(() => {
    analytics.track('pronunciation_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      setLoading(true);
      setError(null);

      const [drillsR, progressR] = await Promise.allSettled([
        fetchPronunciationDrills(difficulty || undefined),
        fetchMyPronunciationProgress(),
      ]);

      if (cancelled) return;

      if (drillsR.status === 'fulfilled') setDrills(drillsR.value as PronunciationDrill[]);
      if (progressR.status === 'fulfilled') {
        const items = progressR.value as PronProgress[];
        setProgressMap(Object.fromEntries(items.map(p => [p.phonemeCode, p])));
      }
      if (drillsR.status === 'rejected') setError('Could not load pronunciation drills.');
      setLoading(false);
    })();

    return () => {
      cancelled = true;
    };
  }, [difficulty]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Pronunciation Drills"
        description="Master English phonemes and sounds for OET"
        icon={Mic}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="flex gap-3 mb-6">
        <select value={difficulty} onChange={e => setDifficulty(e.target.value)} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="">All levels</option>
          <option value="easy">Easy</option>
          <option value="medium">Medium</option>
          <option value="hard">Hard</option>
        </select>
      </div>

      {loading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-xl" />)}
        </div>
      ) : drills.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No pronunciation drills available.</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {drills.map((drill, i) => {
            const prog = progressMap[drill.targetPhoneme];
            const exampleWords = parseExampleWords(drill.exampleWordsJson).slice(0, 4);
            return (
              <motion.div key={drill.id} initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.04 }}>
                <Link href={`/pronunciation/${drill.id}`}>
                  <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5 hover:border-rose-300 dark:hover:border-rose-600 hover:shadow-sm transition-all cursor-pointer group">
                    <div className="flex items-start gap-3">
                      <div className="p-2.5 bg-rose-100 dark:bg-rose-900/30 rounded-lg flex-shrink-0">
                        <Mic className="w-5 h-5 text-rose-600 dark:text-rose-400" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="font-semibold text-gray-900 dark:text-white group-hover:text-rose-600 dark:group-hover:text-rose-400 transition-colors">{drill.label}</span>
                          <span className="text-sm font-mono bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-300 px-1.5 rounded">{drill.targetPhoneme}</span>
                        </div>
                        <div className="flex items-center gap-3 mt-2">
                          <span className={`text-xs px-2 py-0.5 rounded-full font-medium capitalize ${DIFFICULTY_COLORS[drill.difficulty] ?? 'bg-gray-100 text-gray-500'}`}>
                            {drill.difficulty}
                          </span>
                          {prog && (
                            <span className="text-xs text-gray-400">{Math.round(prog.averageScore)}% avg · {prog.attemptCount} attempt{prog.attemptCount !== 1 ? 's' : ''}</span>
                          )}
                        </div>
                        {exampleWords.length > 0 && (
                          <div className="mt-3 flex flex-wrap gap-2">
                            {exampleWords.map(word => (
                              <span key={word} className="rounded-full bg-gray-100 px-2.5 py-1 text-xs font-medium text-gray-600 dark:bg-gray-700 dark:text-gray-300">
                                {word}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>
                      <ChevronRight className="w-4 h-4 text-gray-300 group-hover:text-rose-400 flex-shrink-0 transition-colors mt-1" />
                    </div>
                  </div>
                </Link>
              </motion.div>
            );
          })}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
