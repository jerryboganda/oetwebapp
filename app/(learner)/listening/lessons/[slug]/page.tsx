'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Button } from '@/components/ui/button';
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
      <div className="mx-auto max-w-3xl space-y-6" aria-busy="true">
        <p className="sr-only">Loading lesson…</p>
        <Skeleton className="h-9 w-2/3 rounded-lg" />
        <CardSkeleton />
      </div>
    );
  }

  if (!lesson) {
    return (
      <div className="mx-auto max-w-3xl space-y-4">
        <h1 className="text-2xl font-bold text-navy">Lesson not found</h1>
        <Button asChild size="sm">
          <Link href="/listening/lessons">Back to lesson list</Link>
        </Button>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <span className="eyebrow text-primary">
          Sub-skill {lesson.skillCode}
        </span>
        <h1 className="text-3xl font-bold tracking-tight text-navy">{lesson.title}</h1>
        <p className="mt-1 text-sm text-muted">~{lesson.estimatedMinutes} min</p>
      </header>

      <ol className="space-y-3">
        {STEPS.map((step, i) => (
          <li
            key={step.key}
            className="rounded-xl border border-border bg-surface p-4 shadow-sm flex items-center justify-between"
          >
            <div>
              <span className="text-xs font-mono text-muted">Step {i + 1}</span>
              <p className="font-semibold text-navy">{step.label}</p>
              <p className="text-xs text-muted">~{step.minutes} min</p>
            </div>
            <span className="text-xs text-muted">
              {lesson.progress?.[(step.key + 'Completed') as keyof typeof lesson.progress]
                ? '✓'
                : '·'}
            </span>
          </li>
        ))}
      </ol>

      <MarkdownContent
        markdown={lesson.bodyMarkdownEn}
        className="rounded-2xl border border-border bg-surface p-6 shadow-sm text-navy"
      />

      <Link href="/listening/lessons" className="text-sm text-primary underline">
        ← All lessons
      </Link>
    </div>
  );
}
