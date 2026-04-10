'use client';

import { useEffect, useState } from 'react';
import { BookMarked, CheckCircle2, Clock, Sparkles } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchGrammarLessons } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';

type GrammarLesson = {
  id: string;
  examTypeCode: string;
  title: string;
  description: string | null;
  level: string;
  estimatedMinutes: number;
  status: string;
  category: string | null;
};

const EXAM_TYPES = [
  { value: 'oet', label: 'OET' },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte', label: 'PTE' },
];

const LEVELS = [
  { value: '', label: 'All levels' },
  { value: 'beginner', label: 'Beginner' },
  { value: 'intermediate', label: 'Intermediate' },
  { value: 'advanced', label: 'Advanced' },
];

const LEVEL_ACCENTS: Record<string, 'emerald' | 'amber' | 'rose'> = {
  beginner: 'emerald',
  intermediate: 'amber',
  advanced: 'rose',
};

function titleCase(value: string) {
  return value
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

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

  const selectedExamType = EXAM_TYPES.find((item) => item.value === examType)?.label ?? 'OET';
  const selectedLevel = LEVELS.find((item) => item.value === level)?.label ?? 'All levels';
  const hasActiveFilters = examType !== 'oet' || level !== '';

  const heroHighlights = [
    { icon: BookMarked, label: 'Exam type', value: selectedExamType },
    { icon: Clock, label: 'Level', value: selectedLevel },
    { icon: CheckCircle2, label: 'Lessons', value: loading ? 'Loading…' : `${lessons.length} available` },
  ];

  const resetFilters = () => {
    setExamType('oet');
    setLevel('');
  };

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          title="Grammar Lessons"
          description="Strengthen your grammar for OET and other English exams."
          icon={BookMarked}
          highlights={heroHighlights}
        />

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

        <MotionSection className="space-y-6">
          <Card className="p-5 sm:p-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
              <div className="max-w-2xl space-y-2">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Refine the library</p>
                <h2 className="text-xl font-bold tracking-tight text-navy sm:text-2xl">Keep the lesson set focused on the exam path you need.</h2>
                <p className="text-sm leading-6 text-muted">
                  Use exam and level filters to narrow the lesson set without leaving the dashboard language.
                </p>
              </div>

              {hasActiveFilters ? (
                <Button variant="ghost" size="sm" onClick={resetFilters} className="self-start lg:self-auto">
                  Reset filters
                </Button>
              ) : null}
            </div>

            <div className="mt-6 grid gap-4 lg:grid-cols-2">
              <div className="space-y-3 rounded-2xl border border-border bg-background-light/70 p-4">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Exam type</p>
                <div className="flex flex-wrap gap-2">
                  {EXAM_TYPES.map((item) => {
                    const active = examType === item.value;
                    return (
                      <button
                        key={item.value}
                        type="button"
                        onClick={() => setExamType(item.value)}
                        className={cn(
                          'pressable inline-flex items-center rounded-2xl border px-4 py-2 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                          active
                            ? 'border-primary/20 bg-primary/10 text-primary-dark shadow-sm'
                            : 'border-border bg-surface text-muted hover:bg-white hover:text-navy',
                        )}
                      >
                        {item.label}
                      </button>
                    );
                  })}
                </div>
              </div>

              <div className="space-y-3 rounded-2xl border border-border bg-background-light/70 p-4">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Level</p>
                <div className="flex flex-wrap gap-2">
                  {LEVELS.map((item) => {
                    const active = level === item.value;
                    return (
                      <button
                        key={item.value || 'all'}
                        type="button"
                        onClick={() => setLevel(item.value)}
                        className={cn(
                          'pressable inline-flex items-center rounded-2xl border px-4 py-2 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                          active
                            ? 'border-primary/20 bg-primary/10 text-primary-dark shadow-sm'
                            : 'border-border bg-surface text-muted hover:bg-white hover:text-navy',
                        )}
                      >
                        {item.label}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>
          </Card>

          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Lesson library"
              title={`Browse the ${selectedExamType} grammar set`}
              description={hasActiveFilters ? `Showing ${selectedLevel.toLowerCase()} lessons for ${selectedExamType}.` : 'Showing the default OET-aligned lesson set.'}
              className="mb-4"
            />

            {loading ? (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {Array.from({ length: 6 }).map((_, i) => (
                  <Skeleton key={i} className="h-48 rounded-2xl" />
                ))}
              </div>
            ) : lessons.length === 0 ? (
              <Card className="border-dashed border-border p-8 text-center shadow-sm">
                <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <Sparkles className="h-5 w-5" />
                </div>
                <h3 className="mt-4 text-lg font-bold text-navy">No lessons match this combination</h3>
                <p className="mt-2 text-sm leading-6 text-muted">
                  Try a different exam family or level to surface lessons again.
                </p>
                <Button variant="outline" size="sm" className="mt-6" onClick={resetFilters}>
                  Show all lessons
                </Button>
              </Card>
            ) : (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {lessons.map((lesson, i) => {
                  const accent = LEVEL_ACCENTS[lesson.level] ?? 'slate';

                  return (
                    <MotionItem key={lesson.id} delayIndex={i}>
                      <LearnerSurfaceCard
                        className="h-full"
                        card={{
                          kind: 'navigation',
                          sourceType: 'frontend_navigation',
                          accent,
                          eyebrow: lesson.examTypeCode.toUpperCase(),
                          eyebrowIcon: BookMarked,
                          title: lesson.title,
                          description: lesson.description?.trim() || 'Exam-focused grammar guidance with clear examples and practice.',
                          metaItems: [
                            { label: titleCase(lesson.level) },
                            { label: lesson.category?.trim() || 'Core grammar' },
                            { label: `${lesson.estimatedMinutes} min` },
                          ],
                          primaryAction: {
                            label: 'Open lesson',
                            href: `/grammar/${lesson.id}`,
                          },
                        }}
                      />
                    </MotionItem>
                  );
                })}
              </div>
            )}
          </div>
        </MotionSection>
      </div>
    </LearnerDashboardShell>
  );
}
