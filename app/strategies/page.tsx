'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Lightbulb, ChevronRight, Clock } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchStrategyGuides } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type StrategyGuide = {
  id: string;
  examTypeCode: string;
  subtestCode: string | null;
  title: string;
  summary: string | null;
  category: string;
  readingTimeMinutes: number;
  estimatedMinutes?: number;
  status: string;
};

const SUBTEST_COLORS: Record<string, string> = {
  writing: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  speaking: 'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300',
  reading: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  listening: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
};

export default function StrategiesPage() {
  const [guides, setGuides] = useState<StrategyGuide[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [examType, setExamType] = useState('oet');
  const [subtest, setSubtest] = useState('');

  const formatCategory = (value: string) => value.replace(/_/g, ' ');

  useEffect(() => {
    analytics.track('strategies_page_viewed');
  }, []);

  useEffect(() => {
    fetchStrategyGuides({ examTypeCode: examType, subtestCode: subtest || undefined }).then(data => {
      setGuides(data as StrategyGuide[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load strategy guides.');
      setLoading(false);
    });
  }, [examType, subtest]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Strategy Guides"
        description="Expert strategies to improve your exam performance"
        icon={Lightbulb}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="flex flex-wrap gap-3 mb-6">
        <select value={examType} onChange={e => {
          setError(null);
          setLoading(true);
          setExamType(e.target.value);
        }} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="oet">OET</option>
          <option value="ielts">IELTS</option>
          <option value="pte">PTE</option>
        </select>
        <select value={subtest} onChange={e => {
          setError(null);
          setLoading(true);
          setSubtest(e.target.value);
        }} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="">All Subtests</option>
          <option value="writing">Writing</option>
          <option value="speaking">Speaking</option>
          <option value="reading">Reading</option>
          <option value="listening">Listening</option>
        </select>
      </div>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-xl" />)}
        </div>
      ) : guides.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No strategy guides available for this selection.</div>
      ) : (
        <div className="space-y-3">
          {guides.map((guide, i) => (
            <MotionItem key={guide.id} delayIndex={i}>
              <Link href={`/strategies/${guide.id}`}>
                <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5 hover:border-yellow-300 dark:hover:border-yellow-600 hover:shadow-sm transition-all cursor-pointer group flex items-center gap-4">
                  <div className="p-2.5 bg-yellow-100 dark:bg-yellow-900/30 rounded-lg flex-shrink-0">
                    <Lightbulb className="w-5 h-5 text-yellow-600 dark:text-yellow-400" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-gray-900 dark:text-white group-hover:text-yellow-700 dark:group-hover:text-yellow-400 transition-colors">{guide.title}</div>
                    {guide.summary && <div className="text-sm text-gray-500 mt-0.5 line-clamp-1">{guide.summary}</div>}
                    <div className="flex items-center gap-3 mt-2">
                      {guide.subtestCode && (
                        <span className={`text-xs px-2 py-0.5 rounded-full font-medium capitalize ${SUBTEST_COLORS[guide.subtestCode] ?? 'bg-gray-100 text-gray-500'}`}>
                          {guide.subtestCode}
                        </span>
                      )}
                      <div className="flex items-center gap-1 text-xs text-gray-400">
                        <Clock className="w-3.5 h-3.5" />
                        {guide.estimatedMinutes ?? guide.readingTimeMinutes} min
                      </div>
                      <span className="text-xs text-gray-400 capitalize">{formatCategory(guide.category)}</span>
                    </div>
                  </div>
                  <ChevronRight className="w-5 h-5 text-gray-300 group-hover:text-yellow-400 flex-shrink-0 transition-colors" />
                </div>
              </Link>
            </MotionItem>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
