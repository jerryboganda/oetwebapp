'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Lightbulb, ChevronRight, Clock } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchStrategyGuides } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { Card } from '@/components/ui/card';

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
      <div className="space-y-6">
      <LearnerPageHero
        eyebrow="Learn"
        title="Strategy Guides"
        description="Expert strategies to improve your exam performance."
        icon={Lightbulb}
        highlights={[
          { icon: Lightbulb, label: 'Format', value: 'Expert guides' },
          { icon: Clock, label: 'Filter', value: subtest || 'All subtests' },
          { icon: ChevronRight, label: 'Focus', value: 'Actionable' },
        ]}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <Card className="p-5 shadow-sm">
        <LearnerSurfaceSectionHeader
          eyebrow="Library filters"
          title="Narrow the guide set"
          description="Keep the controls consistent with the learner dashboard surface language."
          className="mb-4"
        />
        <div className="flex flex-wrap gap-3">
          <select value={examType} onChange={e => {
            setError(null);
            setLoading(true);
            setExamType(e.target.value);
          }} className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2">
            <option value="oet">OET</option>
            <option value="ielts">IELTS</option>
            <option value="pte">PTE</option>
          </select>
          <select value={subtest} onChange={e => {
            setError(null);
            setLoading(true);
            setSubtest(e.target.value);
          }} className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2">
            <option value="">All Subtests</option>
            <option value="writing">Writing</option>
            <option value="speaking">Speaking</option>
            <option value="reading">Reading</option>
            <option value="listening">Listening</option>
          </select>
        </div>
      </Card>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-3xl" />)}
        </div>
      ) : guides.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <p className="text-sm text-muted">No strategy guides available for this selection.</p>
        </Card>
      ) : (
        <div>
          <LearnerSurfaceSectionHeader
            eyebrow="Guides"
            title="Expert strategy cards"
            description="Each guide is presented as a calm, actionable surface instead of a generic list item."
            className="mb-4"
          />
          <div className="space-y-3">
          {guides.map((guide, i) => (
            <MotionItem key={guide.id} delayIndex={i}>
              <Link href={`/strategies/${guide.id}`}>
                <div className="group flex cursor-pointer items-center gap-4 rounded-3xl border border-border bg-surface p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-yellow-50 text-yellow-600">
                    <Lightbulb className="h-5 w-5" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-navy transition-colors group-hover:text-yellow-700">{guide.title}</div>
                    {guide.summary && <div className="mt-0.5 line-clamp-1 text-sm text-muted">{guide.summary}</div>}
                    <div className="flex items-center gap-3 mt-2">
                      {guide.subtestCode && (
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${SUBTEST_COLORS[guide.subtestCode] ?? 'bg-gray-100 text-gray-500'}`}>
                          {guide.subtestCode}
                        </span>
                      )}
                      <div className="flex items-center gap-1 text-xs text-muted">
                        <Clock className="h-3.5 w-3.5" />
                        {guide.estimatedMinutes ?? guide.readingTimeMinutes} min
                      </div>
                      <span className="text-xs capitalize text-muted">{formatCategory(guide.category)}</span>
                    </div>
                  </div>
                  <ChevronRight className="h-5 w-5 shrink-0 text-muted transition-colors group-hover:text-yellow-400" />
                </div>
              </Link>
            </MotionItem>
          ))}
          </div>
        </div>
      )}
      </div>
    </LearnerDashboardShell>
  );
}
