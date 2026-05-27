'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight, BookOpenCheck, CheckCircle2, Dumbbell, HelpCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { getWritingLesson, updateWritingLessonProgress, type WritingLessonDetailDto, writingSkillLabels } from '@/lib/writing-pathway-api';

export default function WritingLessonPage() {
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
      .catch(() => setError('Could not load this Writing lesson.'));
  }, [router, slug]);

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
      setError('Could not save lesson progress.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={lesson?.title ?? 'Writing Lesson'}>
      <div className="space-y-8">
        <Button asChild variant="ghost" size="sm"><Link href="/writing/skill-tree"><ArrowLeft className="h-4 w-4" /> Skill tree</Link></Button>

        <LearnerPageHero
          eyebrow={lesson ? `${lesson.skillCode} Foundation` : 'Writing Foundation'}
          icon={BookOpenCheck}
          accent="amber"
          title={lesson?.title ?? 'Writing lesson'}
          description={lesson ? writingSkillLabels[lesson.skillCode] ?? lesson.skillCode : 'Loading lesson'}
          highlights={[
            { icon: CheckCircle2, label: 'Status', value: complete ? 'Complete' : 'In progress' },
            { icon: HelpCircle, label: 'Quiz', value: lesson?.progress?.quizScore == null ? 'Pending' : `${lesson.progress.quizScore}/5` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {lesson ? (
          <>
            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow="Lesson" title="Foundation notes" className="mb-4" />
              <div className="space-y-4 text-sm leading-7 text-navy">
                {bodyBlocks.map((block) => block.startsWith('##') || block.startsWith('###') ? (
                  <h2 key={block} className="text-lg font-bold text-navy">{block.replace(/^#+\s*/, '')}</h2>
                ) : (
                  <p key={block}>{block}</p>
                ))}
              </div>
              <div className="mt-5">
                <Button onClick={() => void saveProgress({ bodyRead: true })} loading={saving} variant={lesson.progress?.bodyRead ? 'outline' : 'primary'}>
                  <CheckCircle2 className="h-4 w-4" /> {lesson.progress?.bodyRead ? 'Read' : 'Mark read'}
                </Button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow="Drill" title="Apply the skill" className="mb-4" />
              <p className="text-sm leading-7 text-navy">{lesson.drillPrompt}</p>
              <div className="mt-5">
                <Button onClick={() => void saveProgress({ drillCompleted: true })} loading={saving} variant={lesson.progress?.drillCompleted ? 'outline' : 'primary'}>
                  <Dumbbell className="h-4 w-4" /> {lesson.progress?.drillCompleted ? 'Drill complete' : 'Mark drill complete'}
                </Button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <LearnerSurfaceSectionHeader eyebrow="Quiz" title="Score check" className="mb-4" />
              <div className="space-y-4">
                {lesson.quiz.map((question) => (
                  <div key={question.id} className="rounded-xl border border-border bg-background p-4">
                    <p className="font-semibold text-navy">{question.prompt}</p>
                    <div className="mt-3 flex flex-wrap gap-2">
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
                      className={`rounded-lg border px-3 py-2 text-sm font-bold ${selectedScore === score ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy'}`}
                    >
                      {score}/5
                    </button>
                  ))}
                  <Button onClick={() => void saveProgress({ quizScore: selectedScore })} loading={saving}>Save quiz score</Button>
                </div>
              </div>
            </section>

            <div className="flex flex-wrap justify-between gap-3">
              {lesson.previousSlug ? <Button asChild variant="outline"><Link href={`/writing/lessons/${lesson.previousSlug}`}><ArrowLeft className="h-4 w-4" /> Previous</Link></Button> : <span />}
              {lesson.nextSlug ? <Button asChild disabled={!complete}><Link href={complete ? `/writing/lessons/${lesson.nextSlug}` : '/writing/skill-tree'}>Next <ArrowRight className="h-4 w-4" /></Link></Button> : <Button asChild><Link href="/writing/pathway">Pathway</Link></Button>}
            </div>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}