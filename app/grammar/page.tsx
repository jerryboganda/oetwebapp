'use client';

import { useEffect, useMemo, useState } from 'react';
import { BookMarked, CheckCircle2, Clock, Sparkles, Trophy } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  GrammarTopicCard,
  GrammarLessonCard,
  GrammarRecommendationStrip,
} from '@/components/domain/grammar';
import {
  fetchGrammarOverview,
  fetchGrammarLessons,
  dismissGrammarRecommendation,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import type {
  GrammarLessonSummary,
  GrammarOverview,
  GrammarTopicLearner,
} from '@/lib/grammar/types';

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

export default function GrammarPage() {
  const [overview, setOverview] = useState<GrammarOverview | null>(null);
  const [lessons, setLessons] = useState<GrammarLessonSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingLessons, setLoadingLessons] = useState(false);
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
        const data = (await fetchGrammarOverview(examType)) as GrammarOverview;
        if (!cancelled) setOverview(data);
      } catch {
        if (!cancelled) setError('Could not load the grammar overview.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [examType]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoadingLessons(true);
      try {
        const data = (await fetchGrammarLessons({ examTypeCode: examType, level: level || undefined })) as GrammarLessonSummary[];
        if (!cancelled) setLessons(Array.isArray(data) ? data : []);
      } catch {
        if (!cancelled) setLessons([]);
      } finally {
        if (!cancelled) setLoadingLessons(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [examType, level]);

  const selectedExamLabel = EXAM_TYPES.find((x) => x.value === examType)?.label ?? 'OET';
  const selectedLevelLabel = LEVELS.find((x) => x.value === level)?.label ?? 'All levels';

  const heroHighlights = useMemo(() => {
    const t = overview?.topics?.length ?? 0;
    const l = lessons.length;
    const m = overview?.lessonsMastered ?? 0;
    return [
      { icon: BookMarked, label: 'Exam path', value: selectedExamLabel },
      { icon: Sparkles, label: 'Topics', value: loading ? 'Loading…' : `${t} available` },
      { icon: CheckCircle2, label: 'Lessons', value: loading ? 'Loading…' : `${l} available` },
      { icon: Trophy, label: 'Mastered', value: loading ? 'Loading…' : `${m} lessons` },
    ];
  }, [overview, lessons.length, selectedExamLabel, loading]);

  const resetFilters = () => {
    setExamType('oet');
    setLevel('');
  };

  async function onDismiss(id: string) {
    try {
      await dismissGrammarRecommendation(id);
      setOverview((prev) => prev ? { ...prev, recommendations: prev.recommendations.filter((r) => r.id !== id) } : prev);
      analytics.track('grammar_recommendation_dismissed', { id });
    } catch {
      // non-fatal
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          title="Grammar Lessons"
          description="Strengthen the grammar patterns that matter for OET and other English exams."
          icon={BookMarked}
          highlights={heroHighlights}
        />

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

        <MotionSection className="space-y-6">
          {overview && overview.recommendations?.length > 0 ? (
            <GrammarRecommendationStrip
              recommendations={overview.recommendations}
              onOpen={(r) => analytics.track('grammar_recommendation_clicked', { id: r.id })}
              onDismiss={(r) => onDismiss(r.id)}
            />
          ) : null}

          {/* Topic-first hub */}
          <section aria-label="Grammar topics">
            <LearnerSurfaceSectionHeader
              eyebrow="Topic path"
              title={`Browse ${selectedExamLabel} grammar topics`}
              description="Build mastery topic by topic. Every lesson is graded server-side and drives your readiness score."
              className="mb-3"
            />
            {loading ? (
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {Array.from({ length: 6 }).map((_, i) => (
                  <Skeleton key={i} className="h-44 rounded-2xl" />
                ))}
              </div>
            ) : (overview?.topics?.length ?? 0) === 0 ? (
              <EmptyTopicState examLabel={selectedExamLabel} />
            ) : (
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {overview!.topics.map((t: GrammarTopicLearner) => (
                  <GrammarTopicCard key={t.id} topic={t} />
                ))}
              </div>
            )}
          </section>

          {/* Filters */}
          <Card className="p-5 sm:p-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
              <div className="max-w-2xl space-y-2">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Refine the library</p>
                <h2 className="text-xl font-bold tracking-tight text-navy sm:text-2xl">Filter by exam and level.</h2>
                <p className="text-sm leading-6 text-muted">Use the filters to narrow the full lesson list below.</p>
              </div>
              {examType !== 'oet' || level !== '' ? (
                <Button variant="ghost" size="sm" onClick={resetFilters} className="self-start lg:self-auto">
                  Reset filters
                </Button>
              ) : null}
            </div>

            <div className="mt-6 grid gap-4 lg:grid-cols-2">
              <FilterGroup label="Exam type">
                {EXAM_TYPES.map((item) => (
                  <FilterChip key={item.value} active={examType === item.value} onClick={() => setExamType(item.value)}>
                    {item.label}
                  </FilterChip>
                ))}
              </FilterGroup>
              <FilterGroup label="Level">
                {LEVELS.map((item) => (
                  <FilterChip key={item.value || 'all'} active={level === item.value} onClick={() => setLevel(item.value)}>
                    {item.label}
                  </FilterChip>
                ))}
              </FilterGroup>
            </div>
          </Card>

          {/* Flat lesson list */}
          <section aria-label="Lesson library">
            <LearnerSurfaceSectionHeader
              eyebrow="Lesson library"
              title={`Browse ${selectedExamLabel} grammar lessons`}
              description={`${selectedLevelLabel} · ${lessons.length} lessons available.`}
              className="mb-4"
            />
            {loadingLessons ? (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {Array.from({ length: 4 }).map((_, i) => (
                  <Skeleton key={i} className="h-44 rounded-2xl" />
                ))}
              </div>
            ) : lessons.length === 0 ? (
              <Card className="border-dashed border-border p-8 text-center shadow-sm">
                <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <Sparkles className="h-5 w-5" />
                </div>
                <h3 className="mt-4 text-lg font-bold text-navy">No lessons match your filter</h3>
                <p className="mt-2 text-sm leading-6 text-muted">Try a different exam family or level.</p>
                <Button variant="outline" size="sm" className="mt-6" onClick={resetFilters}>
                  Show all lessons
                </Button>
              </Card>
            ) : (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {lessons.map((lesson) => (
                  <GrammarLessonCard key={lesson.id} lesson={lesson} />
                ))}
              </div>
            )}
          </section>
        </MotionSection>

        <GlobalProgressFooter overview={overview} loading={loading} />
      </div>
    </LearnerDashboardShell>
  );
}

function FilterGroup({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-3 rounded-2xl border border-border bg-background-light/70 p-4">
      <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{label}</p>
      <div className="flex flex-wrap gap-2">{children}</div>
    </div>
  );
}

function FilterChip({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'pressable inline-flex items-center rounded-2xl border px-4 py-2 text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
        active
          ? 'border-primary/20 bg-primary/10 text-primary-dark shadow-sm'
          : 'border-border bg-surface text-muted hover:bg-white hover:text-navy',
      )}
    >
      {children}
    </button>
  );
}

function EmptyTopicState({ examLabel }: { examLabel: string }) {
  return (
    <Card className="border-dashed border-border p-8 text-center shadow-sm">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
        <Sparkles className="h-5 w-5" />
      </div>
      <h3 className="mt-4 text-lg font-bold text-navy">No {examLabel} topics are published yet</h3>
      <p className="mt-2 text-sm leading-6 text-muted">
        Our content team is finalising the {examLabel} grammar library. Check back soon, or explore the OET library in the meantime.
      </p>
    </Card>
  );
}

function GlobalProgressFooter({ overview, loading }: { overview: GrammarOverview | null; loading: boolean }) {
  if (loading || !overview) return null;
  const pct = Math.round(overview.overallMasteryScore);
  return (
    <Card className="border-border bg-white p-5 dark:border-gray-700 dark:bg-gray-800">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Your grammar progress</p>
          <p className="text-base font-semibold text-navy dark:text-white">
            {overview.lessonsMastered} mastered · {overview.lessonsCompleted} completed · {overview.lessonsTotal} lessons available
          </p>
        </div>
        <div className="min-w-[220px] flex-1">
          <div className="flex items-center justify-between text-xs text-muted">
            <span>Overall mastery</span>
            <span className="font-semibold text-navy dark:text-white">{pct}%</span>
          </div>
          <div className="mt-1 h-2 w-full overflow-hidden rounded-full bg-background-light dark:bg-gray-900">
            <div className="h-full rounded-full bg-gradient-to-r from-primary to-primary-dark" style={{ width: `${Math.min(100, pct)}%` }} aria-hidden />
          </div>
          <div className="mt-2 flex items-center gap-1 text-xs text-muted">
            <Clock className="h-3.5 w-3.5" /> Updated after every attempt
          </div>
        </div>
      </div>
    </Card>
  );
}
