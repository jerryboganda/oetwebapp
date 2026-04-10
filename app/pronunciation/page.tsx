'use client';

import { useEffect, useState } from 'react';
import { Mic, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
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
  const heroHighlights = [
    { icon: Mic, label: 'Focus', value: 'Phonemes' },
    { icon: ChevronRight, label: 'Filter', value: difficulty || 'All levels' },
    { icon: Mic, label: 'Progress', value: 'Tracked' },
  ];

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
      <div className="mx-auto max-w-6xl space-y-6 px-4 py-6">
      <LearnerPageHero
        eyebrow="Learn"
        title="Pronunciation Drills"
        description="Master English phonemes and sounds for OET."
        icon={Mic}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <Card className="p-5 shadow-sm">
        <LearnerSurfaceSectionHeader
          eyebrow="Library filters"
          title="Adjust drill difficulty"
          description="Keep the same dashboard card language across all learning pages."
          className="mb-4"
        />
        <select value={difficulty} onChange={e => setDifficulty(e.target.value)} className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2">
          <option value="">All levels</option>
          <option value="easy">Easy</option>
          <option value="medium">Medium</option>
          <option value="hard">Hard</option>
        </select>
      </Card>

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-3xl" />)}
        </div>
      ) : drills.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <p className="text-sm text-muted">No pronunciation drills available.</p>
        </Card>
      ) : (
        <div>
          <LearnerSurfaceSectionHeader
            eyebrow="Drills"
            title="Pronunciation drill cards"
            description="Each drill shows difficulty and progress in the same soft-surface style as the dashboard."
            className="mb-4"
          />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {drills.map((drill, i) => {
            const prog = progressMap[drill.targetPhoneme];
            const exampleWords = parseExampleWords(drill.exampleWordsJson).slice(0, 4);
            return (
              <MotionItem key={drill.id} delayIndex={i}>
                <Link href={`/pronunciation/${drill.id}`}>
                  <div className="group cursor-pointer rounded-3xl border border-border bg-surface p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
                    <div className="flex items-start gap-3">
                      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <Mic className="h-5 w-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <span className="font-semibold text-navy transition-colors group-hover:text-rose-600">{drill.label}</span>
                          <span className="rounded bg-background-light px-1.5 font-mono text-sm text-muted">{drill.targetPhoneme}</span>
                        </div>
                        <div className="flex items-center gap-3 mt-2">
                          <span className={`rounded-full px-2 py-0.5 text-xs font-medium capitalize ${DIFFICULTY_COLORS[drill.difficulty] ?? 'bg-gray-100 text-gray-500'}`}>
                            {drill.difficulty}
                          </span>
                          {prog && (
                            <span className="text-xs text-muted">{Math.round(prog.averageScore)}% avg · {prog.attemptCount} attempt{prog.attemptCount !== 1 ? 's' : ''}</span>
                          )}
                        </div>
                        {exampleWords.length > 0 && (
                          <div className="mt-3 flex flex-wrap gap-2">
                            {exampleWords.map(word => (
                              <span key={word} className="rounded-full bg-background-light px-2.5 py-1 text-xs font-medium text-muted">
                                {word}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>
                      <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-muted transition-colors group-hover:text-rose-400" />
                    </div>
                  </div>
                </Link>
              </MotionItem>
            );
          })}
          </div>
        </div>
      )}
      </div>
    </LearnerDashboardShell>
  );
}
