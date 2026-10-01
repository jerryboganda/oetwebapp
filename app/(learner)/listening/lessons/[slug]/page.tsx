'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, CheckCircle2, Circle, GraduationCap } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { CardSkeleton, Skeleton } from '@/components/ui/skeleton';
import { apiClient } from '@/lib/api';

interface LessonDetail {
  id: string;
  slug: string;
  title: string;
  skillCode: string;
  estimatedMinutes: number;
  videoUrl: string | null;
  bodyMarkdownEn: string;
  drillQuestionIds: string[];
  quizQuestionIds: string[];
  progress?: {
    videoWatched: boolean;
    bodyRead: boolean;
    drill1Completed: boolean;
    drill2Completed: boolean;
    drill3Completed: boolean;
    quizScore: number | null;
  };
}

const STEPS = [
  { key: 'video', label: '📺 Watch', minutes: 4 },
  { key: 'body', label: '📖 Read', minutes: 3 },
  { key: 'drill1', label: '🎯 Drill 1 (easy)', minutes: 5 },
  { key: 'drill2', label: '🎯 Drill 2 (medium)', minutes: 5 },
  { key: 'drill3', label: '🎯 Drill 3 (hard)', minutes: 6 },
  { key: 'quiz', label: '✅ Mini-quiz', minutes: 2 },
] as const;

export default function ListeningLessonPage() {
  const params = useParams<{ slug: string }>();
  const slug = params?.slug ?? '';
  const [lesson, setLesson] = useState<LessonDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient.get<LessonDetail | null>(`/v1/listening-pathway/lessons/${encodeURIComponent(slug)}`)
      .then((d: LessonDetail | null) => {
        if (!cancelled) {
          setLesson(d);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [slug]);

  if (loading) {
    return (
      <div aria-busy="true" className="learner-page-flow">
        <p className="sr-only">Loading lesson…</p>
        <Skeleton className="h-32 rounded-2xl" />
        <CardSkeleton />
      </div>
    );
  }

  if (!lesson) {
    return (
      <EmptyState
        icon={<GraduationCap className="h-8 w-8" aria-hidden />}
        title="Lesson not found"
        action={{ label: 'Back to lesson list', href: '/listening/lessons' }}
      />
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow={`Sub-skill ${lesson.skillCode}`}
        icon={GraduationCap}
        accent="purple"
        title={lesson.title}
        description={`~${lesson.estimatedMinutes} min`}
      />

      {/* One card for the step list: rows, not a stack of cards. */}
      <MotionSection>
        <Card padding="none">
          <ol className="divide-y divide-border">
            {STEPS.map((step, i) => {
              const done = Boolean(lesson.progress?.[(step.key + 'Completed') as keyof typeof lesson.progress]);
              return (
                <li key={step.key}>
                  <MotionItem delayIndex={Math.min(i, 5)} className="flex items-center justify-between gap-3 px-4 py-3 sm:px-5">
                    <div className="min-w-0">
                      <span className="eyebrow tabular-nums text-muted">Step {i + 1}</span>
                      <p className="font-semibold text-navy">{step.label}</p>
                      <p className="text-xs tabular-nums text-muted">~{step.minutes} min</p>
                    </div>
                    {done ? (
                      <CheckCircle2 className="h-5 w-5 shrink-0 text-success-strong" role="img" aria-label="Completed" />
                    ) : (
                      <Circle className="h-5 w-5 shrink-0 text-border-hover" aria-hidden />
                    )}
                  </MotionItem>
                </li>
              );
            })}
          </ol>
        </Card>
      </MotionSection>

      <MotionSection>
        <Card padding="lg">
          {/* Long-form text: cap the line length, not the page. */}
          <MarkdownContent markdown={lesson.bodyMarkdownEn} className="max-w-prose text-navy" />
        </Card>
      </MotionSection>

      <Link
        href="/listening/lessons"
        className="inline-flex min-h-11 w-fit items-center gap-1.5 rounded-control text-sm font-medium text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden />
        All lessons
      </Link>
    </>
  );
}
