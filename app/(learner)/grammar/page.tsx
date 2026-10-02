'use client';

import { useEffect, useMemo, useState } from 'react';
import { BookMarked, CheckCircle2, Sparkles, Trophy } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { ProgressBar } from '@/components/ui/progress';
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

// ── constants ─────────────────────────────────────────────────────────────
const EXAM_TYPES = [
  { value: 'oet',   label: 'OET'   },
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
    <>
      {/* ── Hero ── */}
      <LearnerPageHero
        eyebrow="Grammar Foundations"
        title="Fix the grammar mistakes that cost you marks"
        description="Targeted OET grammar lessons for healthcare communication. IELTS and PTE grammar foundations remain beta-only and are not exposed during public launch."
        icon={BookMarked}
        highlights={heroHighlights}
      />

      {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

      {/* ── Recommendations ── */}
      {!loading && overview && (overview.recommendations?.length ?? 0) > 0 ? (
        <MotionSection>
          <GrammarRecommendationStrip
            recommendations={overview.recommendations}
            onOpen={(r)   => analytics.track('grammar_recommendation_clicked', { id: r.id })}
            onDismiss={(r) => onDismiss(r.id)}
          />
        </MotionSection>
      ) : null}

      {/* ── Topic grid ── */}
      <MotionSection>
        <section aria-label="Grammar topics" className="space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="Topic path"
            title={`Browse ${selectedExamLabel} grammar topics`}
            description="Build mastery topic by topic. Every completed lesson improves your readiness score."
            // A single exam path is not a choice, so no chip until there are two.
            action={EXAM_TYPES.length > 1 ? (
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
            ) : undefined}
          />

          {loading ? (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
              {Array.from({ length: 6 }).map((_, i) => (
                <Skeleton key={i} className="h-52 rounded-2xl" />
              ))}
            </div>
          ) : (overview?.topics?.length ?? 0) === 0 ? (
            <EmptyState
              icon={<Sparkles className="h-7 w-7 text-primary" aria-hidden="true" />}
              title={`No ${selectedExamLabel} topics published yet`}
              description="Our content team is finalising this library. Check back soon, or explore the OET library."
              action={examType !== 'oet' ? { label: 'Browse OET topics', onClick: () => setExamType('oet') } : undefined}
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
              {overview!.topics.map((t: GrammarTopicLearner, i) => (
                <MotionItem key={t.id} delayIndex={Math.min(i, 5)} className="h-full">
                  <GrammarTopicCard topic={t} />
                </MotionItem>
              ))}
            </div>
          )}
        </section>
      </MotionSection>

      {/* ── Filter + Lesson list ── */}
      <MotionSection>
        <section aria-label="Lesson library" className="space-y-4">
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
                    className="inline-flex min-h-11 items-center rounded-control px-2 text-xs font-semibold text-muted underline underline-offset-2 hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:min-h-9"
                  >
                    Reset
                  </button>
                ) : null}
              </div>
            }
          />

          {loadingLessons ? (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-52 rounded-2xl" />
              ))}
            </div>
          ) : lessons.length === 0 ? (
            <EmptyState
              icon={<Sparkles className="h-7 w-7 text-primary" aria-hidden="true" />}
              title="No lessons match your filter"
              description="Try a different level, or check back when more OET lessons are published."
              action={{ label: 'Show all lessons', onClick: resetFilters }}
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {lessons.map((lesson, i) => (
                <MotionItem key={lesson.id} delayIndex={Math.min(i, 5)} className="h-full">
                  <GrammarLessonCard lesson={lesson} />
                </MotionItem>
              ))}
            </div>
          )}
        </section>
      </MotionSection>

      {/* ── Overall progress footer ── */}
      <GlobalProgressFooter overview={overview} loading={loading} />
    </>
  );
}

// ── sub-components ────────────────────────────────────────────────────────

/** Toggle chip: a primary tint when pressed, a cream surface chip otherwise; 44px tall on touch layouts. */
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
      aria-pressed={active}
      className={cn(
        'pressable inline-flex min-h-11 items-center rounded-full border px-4 py-1.5 text-sm font-semibold transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 lg:min-h-9',
        active
          ? 'border-primary/20 bg-primary/10 text-primary shadow-sm'
          : 'border-border bg-surface text-muted hover:border-primary/20 hover:bg-lavender/30 hover:text-navy',
      )}
    >
      {children}
    </button>
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
    <MotionSection>
      <Card>
        <div className="flex flex-wrap items-center justify-between gap-6">
          <div>
            <p className="eyebrow text-muted">Your grammar progress</p>
            <p className="mt-1 text-base font-bold tabular-nums text-navy">
              {overview.lessonsMastered} mastered ·{' '}
              {overview.lessonsCompleted} completed ·{' '}
              {overview.lessonsTotal} total
            </p>
          </div>
          <div className="min-w-52 flex-1 space-y-2">
            <div className="flex items-center justify-between text-xs font-semibold text-muted">
              <span>Overall mastery</span>
              <span className="text-navy"><CountUp value={pct} suffix="%" /></span>
            </div>
            <ProgressBar value={pct} ariaLabel={`Overall grammar mastery ${pct}%`} color="primary" />
            <p className="text-xs text-muted">Updates after every submitted attempt.</p>
          </div>
        </div>
      </Card>
    </MotionSection>
  );
}
