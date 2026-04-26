'use client';

import { GrammarLessonCard, GrammarRecommendationStrip, GrammarTopicCard } from "@/components/domain/grammar/grammar-cards";
import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import {
    dismissGrammarRecommendation, fetchGrammarLessons, fetchGrammarOverview
} from '@/lib/api';
import type {
    GrammarLessonSummary,
    GrammarOverview,
    GrammarTopicLearner
} from '@/lib/grammar/types';
import { cn } from '@/lib/utils';
import { BookMarked, CheckCircle2, Sparkles, Trophy } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';

// ── constants ─────────────────────────────────────────────────────────────
const EXAM_TYPES = [
  { value: 'oet',   label: 'OET'   },
  { value: 'ielts', label: 'IELTS' },
  { value: 'pte',   label: 'PTE'   },
];

const LEVELS = [
  { value: '',             label: 'All levels'   },
  { value: 'beginner',     label: 'Beginner'     },
  { value: 'intermediate', label: 'Intermediate' },
  { value: 'advanced',     label: 'Advanced'     },
];

// ── page ──────────────────────────────────────────────────────────────────
export default function GrammarPage() {
  const [overview,       setOverview]       = useState<GrammarOverview | null>(null);
  const [lessons,        setLessons]        = useState<GrammarLessonSummary[]>([]);
  const [loading,        setLoading]        = useState(true);
  const [loadingLessons, setLoadingLessons] = useState(false);
  const [error,          setError]          = useState<string | null>(null);
  const [examType,       setExamType]       = useState('oet');
  const [level,          setLevel]          = useState('');

  useEffect(() => { analytics.track('grammar_page_viewed'); }, []);

  // fetch overview when exam type changes
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
    return () => { cancelled = true; };
  }, [examType]);

  // fetch lessons when exam type OR level changes
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
    return () => { cancelled = true; };
  }, [examType, level]);

  const selectedExamLabel = EXAM_TYPES.find((x) => x.value === examType)?.label ?? 'OET';
  const selectedLevelLabel = LEVELS.find((x) => x.value === level)?.label ?? 'All levels';

  const heroHighlights = useMemo(() => {
    const topicCount   = overview?.topics?.length ?? 0;
    const lessonCount  = lessons.length;
    const masteredCount = overview?.lessonsMastered ?? 0;
    return [
      { icon: BookMarked,  label: 'Exam path', value: selectedExamLabel },
      { icon: Sparkles,    label: 'Topics',    value: loading ? 'Loading…' : `${topicCount} available` },
      { icon: CheckCircle2, label: 'Lessons',  value: loading ? 'Loading…' : `${lessonCount} available` },
      { icon: Trophy,      label: 'Mastered',  value: loading ? 'Loading…' : `${masteredCount} lessons` },
    ];
  }, [overview, lessons.length, selectedExamLabel, loading]);

  const resetFilters = () => { setExamType('oet'); setLevel(''); };

  async function onDismiss(id: string) {
    try {
      await dismissGrammarRecommendation(id);
      setOverview((prev) =>
        prev ? { ...prev, recommendations: prev.recommendations.filter((r) => r.id !== id) } : prev,
      );
      analytics.track('grammar_recommendation_dismissed', { id });
    } catch { /* non-fatal */ }
  }

  // ── render ───────────────────────────────────────────────────────────
  return (
    <LearnerDashboardShell pageTitle="Grammar">
      <div className="space-y-8">

        {/* ── Hero ── */}
        <LearnerPageHero
          title="Grammar Lessons"
          description="Strengthen the grammar patterns that matter for OET, IELTS, and PTE — every lesson graded server-side."
          icon={BookMarked}
          highlights={heroHighlights}
        />

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

        <MotionSection className="space-y-8">

          {/* ── Recommendations ── */}
          {!loading && overview && (overview.recommendations?.length ?? 0) > 0 ? (
            <MotionItem>
              <GrammarRecommendationStrip
                recommendations={overview.recommendations}
                onOpen={(r)   => analytics.track('grammar_recommendation_clicked', { id: r.id })}
                onDismiss={(r) => onDismiss(r.id)}
              />
            </MotionItem>
          ) : null}

          {/* ── Topic grid ── */}
          <section aria-label="Grammar topics">
            <LearnerSurfaceSectionHeader
              eyebrow="Topic path"
              title={`Browse ${selectedExamLabel} grammar topics`}
              description="Build mastery topic by topic. Every lesson is graded server-side and drives your readiness score."
              action={
                <div className="flex flex-wrap gap-2">
                  {EXAM_TYPES.map((item) => (
                    <FilterChip
                      key={item.value}
                      active={examType === item.value}
                      onClick={() => setExamType(item.value)}
                    >
                      {item.label}
                    </FilterChip>
                  ))}
                </div>
              }
              className="mb-5"
            />

            {loading ? (
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {Array.from({ length: 6 }).map((_, i) => (
                  <Skeleton key={i} className="h-52 rounded-2xl" />
                ))}
              </div>
            ) : (overview?.topics?.length ?? 0) === 0 ? (
              <EmptyState
                heading={`No ${selectedExamLabel} topics published yet`}
                body="Our content team is finalising this library. Check back soon, or explore the OET library."
                action={<Button variant="outline" size="sm" onClick={() => setExamType('oet')}>Browse OET topics</Button>}
              />
            ) : (
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                {overview!.topics.map((t: GrammarTopicLearner, i) => (
                  <MotionItem key={t.id} delayIndex={i}>
                    <GrammarTopicCard topic={t} />
                  </MotionItem>
                ))}
              </div>
            )}
          </section>

          {/* ── Filter + Lesson list ── */}
          <section aria-label="Lesson library">
            <LearnerSurfaceSectionHeader
              eyebrow="Lesson library"
              title={`${selectedExamLabel} grammar lessons`}
              description={`${selectedLevelLabel} · ${lessons.length} lessons available`}
              action={
                <div className="flex flex-wrap items-center gap-2">
                  {LEVELS.map((item) => (
                    <FilterChip
                      key={item.value || 'all'}
                      active={level === item.value}
                      onClick={() => setLevel(item.value)}
                    >
                      {item.label}
                    </FilterChip>
                  ))}
                  {(examType !== 'oet' || level !== '') ? (
                    <button
                      type="button"
                      onClick={resetFilters}
                      className="text-xs font-semibold text-muted underline underline-offset-2 hover:text-navy"
                    >
                      Reset
                    </button>
                  ) : null}
                </div>
              }
              className="mb-5"
            />

            {loadingLessons ? (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {Array.from({ length: 4 }).map((_, i) => (
                  <Skeleton key={i} className="h-52 rounded-2xl" />
                ))}
              </div>
            ) : lessons.length === 0 ? (
              <EmptyState
                heading="No lessons match your filter"
                body="Try a different exam family or level."
                action={<Button variant="outline" size="sm" onClick={resetFilters}>Show all lessons</Button>}
              />
            ) : (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {lessons.map((lesson, i) => (
                  <MotionItem key={lesson.id} delayIndex={i}>
                    <GrammarLessonCard lesson={lesson} />
                  </MotionItem>
                ))}
              </div>
            )}
          </section>

        </MotionSection>

        {/* ── Overall progress footer ── */}
        <GlobalProgressFooter overview={overview} loading={loading} />

      </div>
    </LearnerDashboardShell>
  );
}

// ── sub-components ────────────────────────────────────────────────────────

/** Active = violet fill chip; inactive = cream surface chip. Matches dashboard CriterionChip token language. */
function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center rounded-full border px-4 py-1.5 text-sm font-semibold transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 active:scale-95',
        active
          ? 'border-primary/20 bg-primary/10 text-primary shadow-sm'
          : 'border-border bg-surface text-muted hover:border-primary/20 hover:bg-lavender/30 hover:text-navy',
      )}
    >
      {children}
    </button>
  );
}

/** Empty state inside a soft dashed card — DESIGN.md §4: "Centered, explanatory, and framed inside a card." */
function EmptyState({
  heading,
  body,
  action,
}: {
  heading: string;
  body: string;
  action?: React.ReactNode;
}) {
  return (
    <Card className="border-dashed border-border p-10 text-center shadow-sm">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
        <Sparkles className="h-5 w-5" />
      </div>
      <h3 className="mt-4 text-lg font-bold text-navy">{heading}</h3>
      <p className="mt-2 text-sm leading-6 text-muted">{body}</p>
      {action ? <div className="mt-6 flex justify-center">{action}</div> : null}
    </Card>
  );
}

/** Bottom-of-page overall mastery strip — uses `bg-surface` (Surface White), navy text, primary progress bar. */
function GlobalProgressFooter({
  overview,
  loading,
}: {
  overview: GrammarOverview | null;
  loading: boolean;
}) {
  if (loading || !overview) return null;
  const pct = Math.min(100, Math.max(0, Math.round(overview.overallMasteryScore ?? 0)));

  return (
    <Card className="bg-surface">
      <div className="flex flex-wrap items-center justify-between gap-6">
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Your grammar progress</p>
          <p className="mt-1 text-base font-bold text-navy">
            {overview.lessonsMastered} mastered ·{' '}
            {overview.lessonsCompleted} completed ·{' '}
            {overview.lessonsTotal} total
          </p>
        </div>
        <div className="min-w-[200px] flex-1 space-y-2">
          <div className="flex items-center justify-between text-xs font-semibold text-muted">
            <span>Overall mastery</span>
            <span className="text-navy">{pct}%</span>
          </div>
          <ProgressBar value={pct} ariaLabel={`Overall grammar mastery ${pct}%`} color="primary" />
          <p className="text-xs text-muted">Updates after every submitted attempt.</p>
        </div>
      </div>
    </Card>
  );
}
