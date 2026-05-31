'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
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
  const t = useTranslations();
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
        setError(err instanceof Error ? err.message : t('writing.lessons.detail.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [lessonId, t]);

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
          setError(err instanceof Error ? err.message : t('writing.lessons.detail.error.sync'));
        }
      }
    },
    [lesson, t],
  );

  if (!lesson && !error) {
    return (
      <LearnerDashboardShell pageTitle={t('writing.lessons.detail.pageTitleFallback')}>
        <div className="p-6 text-sm text-muted">{t('writing.lessons.detail.loading')}</div>
      </LearnerDashboardShell>
    );
  }

  const skillLabel = lesson?.subSkill ?? t('writing.lessons.detail.subSkillFallback');
  const description = t('writing.lessons.detail.descriptionFallback', {
    minutes: lesson?.estimatedMinutes ?? 5,
    skill: skillLabel,
  });

  return (
    <LearnerDashboardShell pageTitle={lesson?.title ?? t('writing.lessons.detail.pageTitleFallback')}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={lesson ? lesson.subSkill : t('writing.lessons.detail.eyebrowFallback')}
          icon={BookOpenCheck}
          accent="amber"
          title={lesson?.title ?? t('writing.lessons.detail.titleFallback')}
          description={description}
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {lesson ? (
          <div dir="ltr">
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
          </div>
        ) : null}

        {lesson && bodyConsumed ? (
          <section aria-labelledby="quiz-heading" className="space-y-3">
            <header className="flex items-center justify-between gap-2">
              <h2 id="quiz-heading" className="text-lg font-bold text-navy">{t('writing.lessons.detail.quizHeading')}</h2>
              {savedCompletion ? <Badge variant="success" size="sm">{t('writing.lessons.detail.saved')}</Badge> : null}
            </header>
            <QuizComponent
              questions={lesson.quizQuestions}
              passPercent={80}
              onSubmit={(score, perQuestion) => void handleQuizSubmit(score, perQuestion)}
            />
          </section>
        ) : null}

        <nav className="flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-label={t('writing.lessons.detail.navAria')}>
          <Button asChild variant="outline" size="sm">
            <Link href="/writing/lessons">
              <ArrowLeft className="h-4 w-4" aria-hidden="true" /> {t('writing.lessons.detail.allLessons')}
            </Link>
          </Button>
          {nextLesson ? (
            <Button asChild size="sm" disabled={quizScore !== null && quizScore < 80}>
              <Link href={`/writing/lessons/${encodeURIComponent(nextLesson.id)}`} aria-label={t('writing.lessons.detail.nextAria', { title: nextLesson.title })}>
                {t('writing.lessons.detail.next', { title: nextLesson.title })} <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
            </Button>
          ) : (
            <Button asChild size="sm" variant="outline">
              <Link href="/writing/skill-tree">{t('writing.lessons.detail.backToTree')}</Link>
            </Button>
          )}
        </nav>
      </div>
    </LearnerDashboardShell>
  );
}
