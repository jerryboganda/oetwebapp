'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookMarked, CheckCircle2, Clock, Sparkles, Trophy } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionPage } from '@/components/ui/motion-primitives';
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

export default function GrammarLessonPage() {
  const params = useParams<{ lessonId: string }>();
  const lessonId = params?.lessonId ?? '';

  const [lesson, setLesson] = useState<GrammarLessonLearner | null>(null);
  const [entitlement, setEntitlement] = useState<GrammarEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<View>('intro');
  const [answers, setAnswers] = useState<Record<string, unknown>>({});
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<GrammarAttemptResult | null>(null);

  useEffect(() => {
    if (!lessonId) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const [data, entitlementResult] = await Promise.all([
          fetchGrammarLesson(lessonId) as Promise<GrammarLessonLearner>,
          fetchGrammarEntitlement().catch(() => null),
        ]);
        if (cancelled) return;
        setLesson(data);
        setEntitlement(entitlementResult);
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
    return () => {
      cancelled = true;
    };
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
      analytics.track('grammar_lesson_completed', { lessonId: lesson.id, score: res.score, masteryScore: res.masteryScore });
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

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="mb-4 h-8 w-48 rounded" />
        <Skeleton className="h-64 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!lesson) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">Lesson not found.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const exerciseCount = lesson.exercises.length;
  const answered = Object.values(answers).filter((v) => {
    if (typeof v === 'string') return v.trim().length > 0;
    if (v && typeof v === 'object') return Object.values(v as Record<string, string>).some((x) => x && String(x).length > 0);
    return false;
  }).length;
  const canSubmit = exerciseCount > 0 && answered === exerciseCount && !submitting;

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3">
        <Link href="/grammar" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300" aria-label="Back to grammar">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <BookMarked className="h-5 w-5 text-primary" />
            <span className="text-xs uppercase tracking-[0.15em] text-muted">{lesson.level}</span>
            <span className="inline-flex items-center gap-1 text-xs text-muted">
              <Clock className="h-3.5 w-3.5" /> {lesson.estimatedMinutes} min
            </span>
          </div>
          <h1 className="truncate text-2xl font-bold text-navy dark:text-white">{lesson.title}</h1>
        </div>
      </div>

      {lesson.description ? (
        <p className="mt-2 text-sm leading-6 text-muted">{lesson.description}</p>
      ) : null}

      {error ? <InlineAlert variant="warning" className="mt-4">{error}</InlineAlert> : null}

      {entitlement ? (
        <div className="mt-4 max-w-3xl">
          <GrammarEntitlementBanner entitlement={entitlement} lessonId={lesson.id} />
        </div>
      ) : null}

      <div className="mt-6 max-w-3xl space-y-6">
        {view === 'intro' ? (
          <Card className="p-6 text-center">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
              <Sparkles className="h-5 w-5" />
            </div>
            <h2 className="mt-4 text-lg font-bold text-navy dark:text-white">Ready to learn?</h2>
            <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted">
              You&apos;ll review the teaching content, then complete {exerciseCount} exercises that are graded by the server.
            </p>
            <Button className="mt-6" onClick={onStart} disabled={entitlement ? !entitlement.allowed : false}>
              {entitlement && !entitlement.allowed ? 'Upgrade to unlock' : 'Start lesson'}
            </Button>
          </Card>
        ) : null}

        {view === 'study' ? (
          <div className="space-y-5">
            <GrammarContentRenderer blocks={lesson.contentBlocks} />
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setView('intro')}>Back</Button>
              <Button onClick={() => setView('practice')} disabled={exerciseCount === 0}>
                {exerciseCount === 0 ? 'No practice available' : `Continue to practice (${exerciseCount})`}
              </Button>
            </div>
            {exerciseCount === 0 ? (
              <p className="text-sm text-muted">This lesson has no practice exercises yet.</p>
            ) : null}
          </div>
        ) : null}

        {view === 'practice' ? (
          <div className="space-y-4">
            <ProgressHeader answered={answered} total={exerciseCount} />
            {lesson.exercises.map((ex) => (
              <GrammarExerciseRunner
                key={ex.id}
                exercise={ex}
                answer={answers[ex.id]}
                disabled={submitting}
                onAnswer={(value) => setAnswers((prev) => ({ ...prev, [ex.id]: value }))}
              />
            ))}
            <div className="flex flex-col items-stretch justify-end gap-2 sm:flex-row">
              <Button variant="outline" onClick={() => setView('study')}>Back to study</Button>
              <Button onClick={onSubmit} disabled={!canSubmit}>
                {submitting ? 'Grading…' : `Submit for grading (${answered}/${exerciseCount})`}
              </Button>
            </div>
          </div>
        ) : null}

        {view === 'result' && result ? (
          <MotionPage className="space-y-5">
            <ResultSummary result={result} />
            <div className="space-y-4">
              {lesson.exercises.map((ex) => {
                const r = resultsByExerciseId.get(ex.id) ?? null;
                return (
                  <GrammarExerciseRunner
                    key={ex.id}
                    exercise={ex}
                    answer={r?.userAnswer ?? answers[ex.id]}
                    disabled
                    onAnswer={() => {}}
                    result={r}
                  />
                );
              })}
            </div>
            <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
              <Button variant="outline" onClick={onRetry}>Try again</Button>
              <Link
                href="/grammar"
                className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
              >
                Back to grammar hub
              </Link>
            </div>
          </MotionPage>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

function ProgressHeader({ answered, total }: { answered: number; total: number }) {
  const pct = total === 0 ? 0 : Math.round((answered / total) * 100);
  return (
    <div>
      <div className="flex items-center justify-between text-xs text-muted">
        <span>Practice progress</span>
        <span className="font-semibold">{answered}/{total}</span>
      </div>
      <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-background-light dark:bg-gray-900">
        <div className="h-full rounded-full bg-primary transition-all" style={{ width: `${pct}%` }} aria-hidden />
      </div>
    </div>
  );
}

function ResultSummary({ result }: { result: GrammarAttemptResult }) {
  const { score, correctCount, incorrectCount, masteryScore, mastered, xpAwarded, reviewItemsCreated } = result;
  return (
    <Card className="border-primary/20 bg-primary/5 p-6 text-center">
      {mastered ? (
        <Trophy className="mx-auto h-10 w-10 text-amber-500" />
      ) : (
        <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-500" />
      )}
      <h2 className="mt-3 text-2xl font-bold text-navy dark:text-white">
        {mastered ? 'Topic mastery achieved!' : 'Lesson complete'}
      </h2>
      <div className="mt-2 text-4xl font-extrabold text-primary">{score}%</div>
      <p className="mt-1 text-sm text-muted">
        {correctCount} correct · {incorrectCount} to review · mastery {masteryScore}%
      </p>
      <div className="mt-3 flex flex-wrap items-center justify-center gap-2 text-xs">
        <span className="rounded-full bg-white px-3 py-1 font-semibold text-primary-dark shadow-sm">+{xpAwarded} XP</span>
        {reviewItemsCreated > 0 ? (
          <span className="rounded-full bg-amber-100 px-3 py-1 font-semibold text-amber-800">
            {reviewItemsCreated} added to your review queue
          </span>
        ) : null}
      </div>
      <div className="mt-4 text-left">
        <SafeRichText
          markdown={mastered
            ? 'Fantastic work. Keep this mastery streak alive by tackling a related topic.'
            : 'Review the explanations below, retry incorrect items, and your mastery score will climb.'}
          className="text-center text-sm text-muted"
        />
      </div>
    </Card>
  );
}
