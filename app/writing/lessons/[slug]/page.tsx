'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ArrowLeft, ArrowRight, BookOpenCheck, CheckCircle2, Dumbbell, HelpCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { getWritingLesson, updateWritingLessonProgress, type WritingLessonDetailDto, writingSkillLabels } from '@/lib/writing-pathway-api';

export default function WritingLessonPage() {
  const t = useTranslations();
  const params = useParams();
  const router = useRouter();
  const slug = typeof params?.slug === 'string' ? params.slug : '';
  const [lesson, setLesson] = useState<WritingLessonDetailDto | null>(null);
  const [selectedScore, setSelectedScore] = useState<number>(4);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!slug) return;
    getWritingLesson(slug)
      .then((nextLesson) => {
        setLesson(nextLesson);
        if (!nextLesson.isUnlocked) router.replace('/writing/skill-tree');
      })
      .catch(() => setError(t('writing.lessons.detail.error.load')));
  }, [router, slug, t]);

  const bodyBlocks = useMemo(() => (lesson?.bodyMarkdownEn ?? '').split('\n\n').filter(Boolean), [lesson]);
  const complete = Boolean(lesson?.progress?.completedAt);

  const saveProgress = async (payload: { bodyRead?: boolean; drillCompleted?: boolean; quizScore?: number }) => {
    if (!lesson) return;
    setSaving(true);
    setError(null);
    try {
      const progress = await updateWritingLessonProgress(lesson.slug, payload);
      setLesson({ ...lesson, progress });
    } catch {
      setError(t('writing.lessons.detail.error.save'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={lesson?.title ?? t('writing.lessons.detail.pageTitleFallback')}>
      <div className="space-y-8">
        <Button asChild variant="ghost" size="sm"><Link href="/writing/skill-tree"><ArrowLeft className="h-4 w-4" /> {t('writing.lessons.detail.back')}</Link></Button>

        <LearnerPageHero
          eyebrow={lesson ? t('writing.lessons.detail.eyebrowWith', { skill: lesson.skillCode }) : t('writing.lessons.detail.eyebrowFoundation')}
          icon={BookOpenCheck}
          accent="amber"
          title={lesson?.title ?? t('writing.lessons.detail.titleFallback')}
          description={lesson ? writingSkillLabels[lesson.skillCode] ?? lesson.skillCode : t('writing.lessons.detail.descriptionLoading')}
          highlights={[
            { icon: CheckCircle2, label: t('writing.lessons.detail.highlights.status'), value: complete ? t('writing.lessons.detail.highlights.statusComplete') : t('writing.lessons.detail.highlights.statusInProgress') },
            { icon: HelpCircle, label: t('writing.lessons.detail.highlights.quiz'), value: lesson?.progress?.quizScore == null ? t('writing.lessons.detail.highlights.quizPending') : t('writing.lessons.detail.highlights.quizScore', { score: lesson.progress.quizScore }) },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {lesson ? (
          <>
            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow={t('writing.lessons.detail.lesson.eyebrow')} title={t('writing.lessons.detail.lesson.title')} className="mb-4" />
              {/* Lesson body markdown is OET-authored English content; force LTR inside RTL chrome. */}
              <div className="space-y-4 text-sm leading-7 text-navy" dir="ltr">
                {bodyBlocks.map((block) => block.startsWith('##') || block.startsWith('###') ? (
                  <h2 key={block} className="text-lg font-bold text-navy">{block.replace(/^#+\s*/, '')}</h2>
                ) : (
                  <p key={block}>{block}</p>
                ))}
              </div>
              <div className="mt-5">
                <Button onClick={() => void saveProgress({ bodyRead: true })} loading={saving} variant={lesson.progress?.bodyRead ? 'outline' : 'primary'}>
                  <CheckCircle2 className="h-4 w-4" /> {lesson.progress?.bodyRead ? t('writing.lessons.detail.lesson.read') : t('writing.lessons.detail.lesson.markRead')}
                </Button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow={t('writing.lessons.detail.drill.eyebrow')} title={t('writing.lessons.detail.drill.title')} className="mb-4" />
              {/* Drill prompt is OET-authored English content. */}
              <p className="text-sm leading-7 text-navy" dir="ltr">{lesson.drillPrompt}</p>
              <div className="mt-5">
                <Button onClick={() => void saveProgress({ drillCompleted: true })} loading={saving} variant={lesson.progress?.drillCompleted ? 'outline' : 'primary'}>
                  <Dumbbell className="h-4 w-4" /> {lesson.progress?.drillCompleted ? t('writing.lessons.detail.drill.done') : t('writing.lessons.detail.drill.mark')}
                </Button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow={t('writing.lessons.detail.quiz.eyebrow')} title={t('writing.lessons.detail.quiz.title')} className="mb-4" />
              <div className="space-y-4">
                {lesson.quiz.map((question) => (
                  <div key={question.id} className="rounded-xl border border-border bg-background p-4">
                    {/* Quiz prompts and options are OET-authored English content. */}
                    <p className="font-semibold text-navy" dir="ltr">{question.prompt}</p>
                    <div className="mt-3 flex flex-wrap gap-2" dir="ltr">
                      {question.options.map((option) => <Badge key={option} variant="muted" size="sm">{option}</Badge>)}
                    </div>
                  </div>
                ))}
                <div className="flex flex-wrap items-center gap-2">
                  {[3, 4, 5].map((score) => (
                    <button
                      key={score}
                      type="button"
                      onClick={() => setSelectedScore(score)}
                      className={`rounded-lg border px-3 py-2 text-sm font-bold ${selectedScore === score ? 'border-primary bg-primary text-white dark:bg-violet-700' : 'border-border bg-background text-navy'}`}
                    >
                      {t('writing.lessons.detail.quiz.optionLabel', { score })}
                    </button>
                  ))}
                  <Button onClick={() => void saveProgress({ quizScore: selectedScore })} loading={saving}>{t('writing.lessons.detail.quiz.saveScore')}</Button>
                </div>
              </div>
            </section>

            <div className="flex flex-wrap justify-between gap-3">
              {lesson.previousSlug ? <Button asChild variant="outline"><Link href={`/writing/lessons/${lesson.previousSlug}`}><ArrowLeft className="h-4 w-4" /> {t('writing.lessons.detail.nav.previous')}</Link></Button> : <span />}
              {lesson.nextSlug ? <Button asChild disabled={!complete}><Link href={complete ? `/writing/lessons/${lesson.nextSlug}` : '/writing/skill-tree'}>{t('writing.lessons.detail.nav.next')} <ArrowRight className="h-4 w-4" /></Link></Button> : <Button asChild><Link href="/writing/pathway">{t('writing.lessons.detail.nav.pathway')}</Link></Button>}
            </div>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
