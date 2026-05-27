'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
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

export default function WritingLessonsCataloguePage() {
  return (
    <Suspense fallback={<LearnerDashboardShell pageTitle="Writing Lessons"><div className="p-6 text-sm text-muted">Loading lessons…</div></LearnerDashboardShell>}>
      <WritingLessonsCatalogueInner />
    </Suspense>
  );
}

function WritingLessonsCatalogueInner() {
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
        setError(err instanceof Error ? err.message : 'Could not load lessons.');
      })
      .finally(() => {
        if (cancelled) return;
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [filter]);

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
    <LearnerDashboardShell pageTitle="Writing Lessons">
      <div className="space-y-6" aria-busy={loading}>
        <LearnerPageHero
          eyebrow="Foundation"
          icon={BookOpen}
          accent="amber"
          title="Writing micro-lessons"
          description="Five-to-ten minute lessons grouped by sub-skill. Each ends with a 5-question quiz."
          highlights={[
            { icon: BookOpen, label: 'Total', value: `${lessons.length}` },
            { icon: CheckCircle2, label: 'Completed', value: `${completions.length}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerSurfaceSectionHeader
          eyebrow="Filter"
          title="By sub-skill"
          description="Click a skill to focus the list. Empty filter shows all lessons."
        />

        <fieldset className="flex flex-wrap items-center gap-2" aria-label="Filter lessons by sub-skill">
          <legend className="sr-only">Filter lessons</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">
            <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" />
            Skill:
          </span>
          <button
            type="button"
            onClick={() => applyFilter(null)}
            aria-pressed={filter === null}
            className={`rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${filter === null ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
          >
            All
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

        <ul className="grid gap-3 md:grid-cols-2" aria-label="Writing lessons">
          {lessons.length === 0 ? (
            <li className="col-span-full">
              <Card padding="lg">
                <CardContent>
                  <p className="text-sm text-muted">No lessons match the filter yet.</p>
                </CardContent>
              </Card>
            </li>
          ) : null}
          {lessons.map((lesson) => {
            const completion = completionMap.get(lesson.id);
            const isComplete = !!completion;
            return (
              <li key={lesson.id}>
                <Card padding="md" aria-label={`Lesson ${lesson.title}`}>
                  <CardContent>
                    <header className="flex items-center justify-between gap-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="muted" size="sm">{lesson.subSkill}</Badge>
                        {isComplete ? <Badge variant="success" size="sm">Completed ({completion.quizScore}%)</Badge> : null}
                      </div>
                      <span className="inline-flex items-center gap-1 text-xs font-bold text-muted">
                        <Clock className="h-3 w-3" aria-hidden="true" /> {lesson.estimatedMinutes} min
                      </span>
                    </header>
                    <h2 className="mt-2 text-base font-bold text-navy">{lesson.title}</h2>
                    <div className="mt-3">
                      <Button asChild size="sm" variant={isComplete ? 'outline' : 'primary'}>
                        <Link href={`/writing/lessons/${encodeURIComponent(lesson.id)}`}>
                          {isComplete ? 'Review lesson' : 'Open lesson'}
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
