'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { BookOpen, CheckCircle2, Clock, Filter } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { listWritingLessons } from '@/lib/writing/api';
import type {
  WritingLessonCompletionDto,
  WritingLessonDto,
  WritingSubSkill,
} from '@/lib/writing/types';

const SKILLS: WritingSubSkill[] = ['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'];

function LessonsLoadingFallback() {
  const t = useTranslations();
  return (
    <LearnerDashboardShell pageTitle={t('writing.lessons.pageTitle')}>
      <div className="p-6 text-sm text-muted">{t('writing.lessons.loading')}</div>
    </LearnerDashboardShell>
  );
}

export default function WritingLessonsCataloguePage() {
  return (
    <Suspense fallback={<LessonsLoadingFallback />}>
      <WritingLessonsCatalogueInner />
    </Suspense>
  );
}

function WritingLessonsCatalogueInner() {
  const t = useTranslations();
  const router = useRouter();
  const searchParams = useSearchParams();
  const initialSkill = (searchParams?.get('subSkill') as WritingSubSkill | null) ?? null;
  const [filter, setFilter] = useState<WritingSubSkill | null>(initialSkill);
  const [lessons, setLessons] = useState<WritingLessonDto[]>([]);
  const [completions, setCompletions] = useState<WritingLessonCompletionDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listWritingLessons(filter ? { subSkill: filter } : {})
      .then((result) => {
        if (cancelled) return;
        setLessons(result.items);
        setCompletions(result.completions);
        setError(null);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.lessons.error.load'));
      })
      .finally(() => {
        if (cancelled) return;
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [filter, t]);

  const completionMap = useMemo(() => {
    const m = new Map<string, WritingLessonCompletionDto>();
    for (const c of completions) m.set(c.lessonId, c);
    return m;
  }, [completions]);

  const applyFilter = (skill: WritingSubSkill | null) => {
    setFilter(skill);
    const next = new URLSearchParams(searchParams?.toString() ?? '');
    if (skill) next.set('subSkill', skill);
    else next.delete('subSkill');
    router.replace(`/writing/lessons${next.size > 0 ? `?${next.toString()}` : ''}`, { scroll: false });
  };

  return (
    <LearnerDashboardShell pageTitle={t('writing.lessons.pageTitle')}>
      <div className="space-y-6" aria-busy={loading}>
        <LearnerPageHero
          eyebrow={t('writing.lessons.eyebrow')}
          icon={BookOpen}
          accent="amber"
          title={t('writing.lessons.catalogue.title')}
          description={t('writing.lessons.catalogue.description')}
          highlights={[
            { icon: BookOpen, label: t('writing.lessons.highlights.total'), value: `${lessons.length}` },
            { icon: CheckCircle2, label: t('writing.lessons.highlights.completed'), value: `${completions.length}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerSurfaceSectionHeader
          eyebrow={t('writing.lessons.filter.eyebrow')}
          title={t('writing.lessons.filter.title')}
          description={t('writing.lessons.filter.description')}
        />

        <fieldset className="flex flex-wrap items-center gap-2" aria-label={t('writing.lessons.filter.label')}>
          <legend className="sr-only">{t('writing.lessons.filter.legend')}</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">
            <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" />
            {t('writing.lessons.filter.skillLabel')}
          </span>
          <button
            type="button"
            onClick={() => applyFilter(null)}
            aria-pressed={filter === null}
            className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${filter === null ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
          >
            {t('writing.lessons.filter.all')}
          </button>
          {SKILLS.map((skill) => (
            <button
              key={skill}
              type="button"
              onClick={() => applyFilter(skill)}
              aria-pressed={filter === skill}
              className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${filter === skill ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
            >
              {skill}
            </button>
          ))}
        </fieldset>

        <ul className="grid gap-3 md:grid-cols-2" aria-label={t('writing.lessons.list.label')}>
          {lessons.length === 0 ? (
            <li className="col-span-full">
              <Card padding="lg">
                <CardContent>
                  <p className="text-sm text-muted">{t('writing.lessons.list.empty')}</p>
                </CardContent>
              </Card>
            </li>
          ) : null}
          {lessons.map((lesson) => {
            const completion = completionMap.get(lesson.id);
            const isComplete = !!completion;
            return (
              <li key={lesson.id}>
                <Card padding="md" aria-label={t('writing.lessons.list.aria', { title: lesson.title })}>
                  <CardContent>
                    <header className="flex items-center justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="muted" size="sm">{lesson.subSkill}</Badge>
                        {isComplete ? <Badge variant="success" size="sm">{t('writing.lessons.list.completed', { score: completion.quizScore })}</Badge> : null}
                      </div>
                      <span className="inline-flex items-center gap-1 text-xs font-bold text-muted">
                        <Clock className="h-3 w-3" aria-hidden="true" /> {t('writing.lessons.list.minutes', { minutes: lesson.estimatedMinutes })}
                      </span>
                    </header>
                    {/* Lesson title is OET-authored English content; force LTR inside RTL chrome. */}
                    <h2 className="mt-2 text-base font-bold text-navy" dir="ltr">{lesson.title}</h2>
                    <div className="mt-3">
                      <Button asChild size="sm" variant={isComplete ? 'outline' : 'primary'}>
                        <Link href={`/writing/lessons/${encodeURIComponent(lesson.id)}`}>
                          {isComplete ? t('writing.lessons.list.review') : t('writing.lessons.list.open')}
                        </Link>
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              </li>
            );
          })}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
