'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, BookOpenCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LessonViewer } from '@/components/domain/writing/LessonViewer';
import { QuizComponent } from '@/components/domain/writing/QuizComponent';
import { completeWritingLesson, getWritingLesson, listWritingLessons } from '@/lib/writing/api';
import type { WritingLessonDto } from '@/lib/writing/types';

export default function WritingLessonDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const lessonId = String(params?.id ?? '');

  const [lesson, setLesson] = useState<WritingLessonDto | null>(null);
  const [nextLesson, setNextLesson] = useState<WritingLessonDto | null>(null);
  const [bodyConsumed, setBodyConsumed] = useState(false);
  const [quizScore, setQuizScore] = useState<number | null>(null);
  const [savedCompletion, setSavedCompletion] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!lessonId) return;
    let cancelled = false;
    void getWritingLesson(lessonId)
      .then(async (l) => {
        if (cancelled) return;
        setLesson(l);
        try {
          const all = await listWritingLessons({ subSkill: l.subSkill });
          if (cancelled) return;
          const list = all.items.slice().sort((a, b) => a.orderInCourse - b.orderInCourse);
          const idx = list.findIndex((x) => x.id === l.id);
          setNextLesson(idx >= 0 && idx + 1 < list.length ? list[idx + 1] : null);
        } catch {
          /* not fatal */
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load this lesson.');
      });
    return () => {
      cancelled = true;
    };
  }, [lessonId]);

  const handleQuizSubmit = useCallback(
    async (score: number, perQuestion: { questionId: string; selectedIndex: number | null; correct: boolean }[]) => {
      if (!lesson) return;
      setQuizScore(score);
      if (score >= 80) {
        try {
          await completeWritingLesson(lesson.id, {
            quizScore: score,
            quizAnswers: perQuestion.map((q) => q.selectedIndex ?? -1),
          });
          setSavedCompletion(true);
        } catch (err) {
          setError(err instanceof Error ? err.message : 'Quiz score recorded locally; sync failed.');
        }
      }
    },
    [lesson],
  );

  if (!lesson && !error) {
    return (
      <LearnerDashboardShell pageTitle="Writing Lesson">
        <div className="p-6 text-sm text-muted">Loading lesson…</div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={lesson?.title ?? 'Writing Lesson'}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={lesson ? lesson.subSkill : 'Foundation'}
          icon={BookOpenCheck}
          accent="amber"
          title={lesson?.title ?? 'Lesson'}
          description={`A focused ${lesson?.estimatedMinutes ?? 5}-minute lesson on ${lesson?.subSkill ?? 'a Writing sub-skill'}, followed by a 5-question quiz.`}
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {lesson ? (
          <LessonViewer
            lesson={{
              id: lesson.id,
              title: lesson.title,
              bodyMarkdown: lesson.bodyMarkdown,
              estimatedMinutes: lesson.estimatedMinutes,
              videoUrl: lesson.videoUrl,
            }}
            onComplete={() => setBodyConsumed(true)}
            bodyConsumed={bodyConsumed}
          />
        ) : null}

        {lesson && bodyConsumed ? (
          <section aria-labelledby="quiz-heading" className="space-y-3">
            <header className="flex items-center justify-between gap-2">
              <h2 id="quiz-heading" className="text-lg font-bold text-navy">Lesson quiz</h2>
              {savedCompletion ? <Badge variant="success" size="sm">Saved</Badge> : null}
            </header>
            <QuizComponent
              questions={lesson.quizQuestions}
              passPercent={80}
              onSubmit={(score, perQuestion) => void handleQuizSubmit(score, perQuestion)}
            />
          </section>
        ) : null}

        <nav className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-label="Lesson navigation">
          <Button asChild variant="outline" size="sm">
            <Link href="/writing/lessons">
              <ArrowLeft className="h-4 w-4" aria-hidden="true" /> All lessons
            </Link>
          </Button>
          {nextLesson ? (
            <Button asChild size="sm" disabled={quizScore !== null && quizScore < 80}>
              <Link href={`/writing/lessons/${encodeURIComponent(nextLesson.id)}`} aria-label={`Next lesson: ${nextLesson.title}`}>
                Next: {nextLesson.title} <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
            </Button>
          ) : (
            <Button asChild size="sm" variant="outline">
              <Link href="/writing/skill-tree">Back to skill tree</Link>
            </Button>
          )}
        </nav>
      </div>
    </LearnerDashboardShell>
  );
}
