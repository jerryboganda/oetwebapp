'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookMarked, CheckCircle2, Clock, Sparkles, Target, Trophy } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionPage, MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { ProgressBar } from '@/components/ui/progress';
import {
  GrammarContentRenderer,
  GrammarEntitlementBanner,
  GrammarExerciseRunner,
  SafeRichText,
} from '@/components/domain/grammar';
import {
  fetchGrammarEntitlement,
  fetchGrammarLesson,
  startGrammarLesson,
  submitGrammarAttempt,
  type GrammarEntitlement,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type {
  GrammarAttemptResult,
  GrammarExerciseResult,
  GrammarLessonLearner,
} from '@/lib/grammar/types';

type View = 'intro' | 'study' | 'practice' | 'result';

// ─────────────────────────────────────────────────────────────────────────
export default function GrammarLessonPage() {
  const params   = useParams<{ lessonId: string }>();
  const lessonId = params?.lessonId ?? '';

  const [lesson,      setLesson]      = useState<GrammarLessonLearner | null>(null);
  const [entitlement, setEntitlement] = useState<GrammarEntitlement | null>(null);
  const [loading,     setLoading]     = useState(true);
  const [error,       setError]       = useState<string | null>(null);
  const [view,        setView]        = useState<View>('intro');
  const [answers,     setAnswers]     = useState<Record<string, unknown>>({});
  const [submitting,  setSubmitting]  = useState(false);
  const [result,      setResult]      = useState<GrammarAttemptResult | null>(null);

  useEffect(() => {
    if (!lessonId) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const [data, ent] = await Promise.all([
          fetchGrammarLesson(lessonId) as Promise<GrammarLessonLearner>,
          fetchGrammarEntitlement().catch(() => null),
        ]);
        if (cancelled) return;
        setLesson(data);
        setEntitlement(ent);
        setAnswers({});
        setResult(null);
        setView(data.progress?.status === 'in_progress' ? 'study' : 'intro');
        analytics.track('grammar_lesson_viewed', { lessonId: data.id });
      } catch {
        if (!cancelled) setError('Could not load this lesson.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [lessonId]);

  const onStart = useCallback(async () => {
    if (!lesson) return;
    try {
      await startGrammarLesson(lesson.id);
      analytics.track('grammar_lesson_started', { lessonId: lesson.id });
      setView('study');
    } catch {
      setError('Could not start this lesson.');
    }
  }, [lesson]);

  const onSubmit = useCallback(async () => {
    if (!lesson || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      const res = (await submitGrammarAttempt(lesson.id, answers)) as GrammarAttemptResult;
      setResult(res);
      setView('result');
      analytics.track('grammar_lesson_completed', {
        lessonId: lesson.id,
        score: res.score,
        masteryScore: res.masteryScore,
      });
      if (res.mastered) analytics.track('grammar_lesson_mastered', { lessonId: lesson.id });
    } catch (e) {
      setError((e as Error).message ?? 'Could not grade your attempt.');
    } finally {
      setSubmitting(false);
    }
  }, [lesson, answers, submitting]);

  const onRetry = useCallback(() => {
    setResult(null);
    setAnswers({});
    setView('practice');
  }, []);

  const resultsByExerciseId = useMemo(() => {
    const map = new Map<string, GrammarExerciseResult>();
    result?.exercises.forEach((r) => map.set(r.exerciseId, r));
    return map;
  }, [result]);

  // ── loading ────────────────────────────────────────────────────────
  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-64 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!lesson) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4">
          <BackLink />
          <InlineAlert variant="warning">Lesson not found.</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  const exerciseCount = lesson.exercises.length;
  const answered      = Object.values(answers).filter((v) => {
    if (typeof v === 'string') return v.trim().length > 0;
    if (v && typeof v === 'object') return Object.values(v as Record<string, string>).some((x) => x && String(x).length > 0);
    return false;
  }).length;
  const canSubmit    = exerciseCount > 0 && answered === exerciseCount && !submitting;
  const isBlocked    = entitlement ? !entitlement.allowed : false;
  const masteryScore = lesson.progress?.masteryScore ?? 0;

  // Hero highlight chips — same token pattern as dashboard.
  const heroHighlights = [
    { icon: Target,        label: 'Level',     value: lesson.level },
    { icon: Clock,         label: 'Duration',  value: `${lesson.estimatedMinutes} min` },
    { icon: Sparkles,      label: 'Exercises', value: `${exerciseCount} exercises` },
    ...(lesson.progress
      ? [{ icon: CheckCircle2, label: 'Mastery', value: `${Math.round(masteryScore)}%` }]
      : []),
  ];

  // ── render ─────────────────────────────────────────────────────────
  return (
    <LearnerDashboardShell>
      <div className="space-y-8">

        {/* Back breadcrumb */}
        <BackLink topicSlug={lesson.topicSlug} topicName={lesson.topicName} />

        {/* ── Hero — same visual contract as every learner page ── */}
        <LearnerPageHero
          eyebrow="Grammar lesson"
          icon={BookMarked}
          accent="primary"
          title={lesson.title}
          description={lesson.description ?? `Strengthen ${lesson.topicName ?? lesson.category} patterns graded server-side.`}
          highlights={heroHighlights}
        />

        {/* Paywall banner — only renders when free tier is exhausted */}
        {entitlement ? (
          <GrammarEntitlementBanner entitlement={entitlement} lessonId={lesson.id} />
        ) : null}

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

        {/* ── Content area — max 3xl keeps it readable ── */}
        <div className="max-w-3xl space-y-6">

          {/* ── INTRO view ── */}
          {view === 'intro' ? (
            <MotionItem>
              <Card>
                <div className="flex flex-col items-center gap-5 py-4 text-center sm:py-6">
                  {/* Icon tile matching dashboard task-card icon style */}
                  <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                    <Sparkles className="h-7 w-7" />
                  </div>

                  <div className="space-y-2">
                    <h2 className="text-xl font-bold text-navy">Ready to learn?</h2>
                    <p className="mx-auto max-w-md text-sm leading-6 text-muted">
                      Review the teaching notes, then complete{' '}
                      <span className="font-semibold text-navy">{exerciseCount} exercises</span>{' '}
                      graded instantly by the server.
                    </p>
                  </div>

                  {lesson.progress && masteryScore > 0 ? (
                    <div className="w-full max-w-xs space-y-1.5">
                      <div className="flex items-center justify-between text-xs font-semibold text-muted">
                        <span>Previous mastery</span>
                        <span className="text-navy">{Math.round(masteryScore)}%</span>
                      </div>
                      <ProgressBar
                        value={masteryScore}
                        ariaLabel={`Previous mastery ${Math.round(masteryScore)}%`}
                        color={masteryScore >= 80 ? 'success' : 'primary'}
                      />
                    </div>
                  ) : null}

                  {lesson.mastered ? (
                    <Badge variant="success" size="md">
                      <Trophy className="mr-1.5 h-3.5 w-3.5" /> Mastered
                    </Badge>
                  ) : null}

                  <Button
                    variant="primary"
                    size="sm"
                    className="min-w-[140px]"
                    onClick={onStart}
                    disabled={isBlocked}
                  >
                    {isBlocked ? 'Upgrade to unlock' : 'Start lesson'}
                  </Button>
                </div>
              </Card>
            </MotionItem>
          ) : null}

          {/* ── STUDY view ── */}
          {view === 'study' ? (
            <MotionItem className="space-y-5">
              {/* Teaching content inside cream surface cards */}
              <GrammarContentRenderer blocks={lesson.contentBlocks} />

              <div className="flex items-center justify-between gap-3 border-t border-border pt-4">
                <Button variant="outline" size="sm" onClick={() => setView('intro')}>
                  <ArrowLeft className="mr-1.5 h-4 w-4" /> Back
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={() => setView('practice')}
                  disabled={exerciseCount === 0}
                >
                  {exerciseCount === 0
                    ? 'No exercises yet'
                    : `Practise now · ${exerciseCount} exercises`}
                </Button>
              </div>

              {exerciseCount === 0 ? (
                <p className="text-sm text-muted">Exercises for this lesson are coming soon.</p>
              ) : null}
            </MotionItem>
          ) : null}

          {/* ── PRACTICE view ── */}
          {view === 'practice' ? (
            <div className="space-y-5">
              {/* Inline progress strip — uses primary bar on cream background */}
              <AnswerProgress answered={answered} total={exerciseCount} />

              {lesson.exercises.map((ex, i) => (
                <MotionItem key={ex.id} delayIndex={i}>
                  <GrammarExerciseRunner
                    exercise={ex}
                    answer={answers[ex.id]}
                    disabled={submitting}
                    onAnswer={(value) =>
                      setAnswers((prev) => ({ ...prev, [ex.id]: value }))
                    }
                  />
                </MotionItem>
              ))}

              <div className="flex flex-col items-stretch justify-end gap-2 border-t border-border pt-4 sm:flex-row">
                <Button variant="outline" size="sm" onClick={() => setView('study')}>
                  Back to study notes
                </Button>
                <Button
                  variant="primary"
                  size="sm"
                  onClick={onSubmit}
                  disabled={!canSubmit}
                  className="min-w-[180px]"
                >
                  {submitting
                    ? 'Grading…'
                    : canSubmit
                      ? `Submit (${answered}/${exerciseCount} answered)`
                      : `Answer all exercises (${answered}/${exerciseCount})`}
                </Button>
              </div>
            </div>
          ) : null}

          {/* ── RESULT view ── */}
          {view === 'result' && result ? (
            <MotionPage className="space-y-6">
              <ResultSummary result={result} />

              {/* Per-exercise results */}
              <div className="space-y-4">
                {lesson.exercises.map((ex, i) => {
                  const r = resultsByExerciseId.get(ex.id) ?? null;
                  return (
                    <MotionItem key={ex.id} delayIndex={i}>
                      <GrammarExerciseRunner
                        exercise={ex}
                        answer={r?.userAnswer ?? answers[ex.id]}
                        disabled
                        onAnswer={() => {}}
                        result={r}
                      />
                    </MotionItem>
                  );
                })}
              </div>

              <div className="flex flex-col gap-2 border-t border-border pt-4 sm:flex-row sm:justify-end">
                <Button variant="outline" size="sm" onClick={onRetry}>Try again</Button>
                <Link href="/grammar">
                  <Button variant="primary" size="sm">Back to grammar hub</Button>
                </Link>
              </div>
            </MotionPage>
          ) : null}

        </div>
      </div>
    </LearnerDashboardShell>
  );
}

// ── sub-components ────────────────────────────────────────────────────────

function BackLink({
  topicSlug,
  topicName,
}: {
  topicSlug?: string | null;
  topicName?: string | null;
}) {
  const href  = topicSlug ? `/grammar/topics/${encodeURIComponent(topicSlug)}` : '/grammar';
  const label = topicName ?? 'Back to grammar';
  return (
    <Link
      href={href}
      className="inline-flex items-center gap-2 text-sm font-semibold text-muted transition-colors hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
    >
      <ArrowLeft className="h-4 w-4" />
      {label}
    </Link>
  );
}

/** Slim progress strip above exercises — cream background, primary fill. */
function AnswerProgress({ answered, total }: { answered: number; total: number }) {
  const pct = total === 0 ? 0 : Math.round((answered / total) * 100);
  return (
    <Card padding="sm">
      <div className="flex items-center justify-between text-xs font-semibold text-muted">
        <span>Exercises answered</span>
        <span className="text-navy">{answered} / {total}</span>
      </div>
      <div className="mt-2">
        <ProgressBar value={pct} ariaLabel={`${answered} of ${total} exercises answered`} color="primary" />
      </div>
    </Card>
  );
}

/** Result summary card — cream surface, violet score, soft amber/emerald badges. */
function ResultSummary({ result }: { result: GrammarAttemptResult }) {
  const { score, correctCount, incorrectCount, masteryScore, mastered, xpAwarded, reviewItemsCreated } = result;

  return (
    <Card className="text-center">
      <div className="flex flex-col items-center gap-4 py-2">
        {/* Trophy or check icon */}
        <div className={`flex h-16 w-16 items-center justify-center rounded-2xl ${mastered ? 'bg-warning/10 text-warning' : 'bg-success/10 text-success'}`}>
          {mastered
            ? <Trophy    className="h-8 w-8" />
            : <CheckCircle2 className="h-8 w-8" />}
        </div>

        {/* Score */}
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">
            {mastered ? 'Mastery achieved!' : 'Lesson complete'}
          </p>
          <div className="mt-1 text-5xl font-extrabold text-primary">{score}%</div>
          <p className="mt-2 text-sm text-muted">
            {correctCount} correct · {incorrectCount} to review · mastery {masteryScore}%
          </p>
        </div>

        {/* Reward chips — same style as dashboard engagement row */}
        <div className="flex flex-wrap items-center justify-center gap-2">
          <span className="inline-flex items-center gap-1 rounded-full border border-primary/20 bg-primary/10 px-3 py-1 text-xs font-bold text-primary">
            +{xpAwarded} XP
          </span>
          {reviewItemsCreated > 0 ? (
            <span className="inline-flex items-center gap-1 rounded-full border border-warning/30 bg-warning/10 px-3 py-1 text-xs font-bold text-warning">
              {reviewItemsCreated} added to review queue
            </span>
          ) : null}
          {mastered ? (
            <span className="inline-flex items-center gap-1 rounded-full border border-success/30 bg-success/10 px-3 py-1 text-xs font-bold text-success">
              <Trophy className="h-3 w-3" /> Mastered
            </span>
          ) : null}
        </div>

        {/* Coaching text */}
        <SafeRichText
          markdown={
            mastered
              ? 'Brilliant work — mastery level reached. Tackle a related topic to build on this momentum.'
              : 'Review the explanations below. Retry the incorrect items and your mastery score will climb.'
          }
          className="max-w-sm text-center text-sm leading-6 text-muted"
        />
      </div>
    </Card>
  );
}
