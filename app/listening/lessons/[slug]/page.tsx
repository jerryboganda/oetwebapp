'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';

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
    fetch(`/v1/listening-pathway/lessons/${encodeURIComponent(slug)}`)
      .then((res) => (res.ok ? res.json() : null))
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
      <main className="mx-auto max-w-3xl px-4 py-12">
        <p className="text-slate-500">Loading lesson…</p>
      </main>
    );
  }

  if (!lesson) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold">Lesson not found</h1>
        <Link href="/listening/lessons" className="rounded-md bg-slate-900 px-4 py-2 text-white text-sm inline-block">
          Back to lesson list
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <header>
        <span className="text-xs font-semibold uppercase tracking-wide text-blue-700">
          Sub-skill {lesson.skillCode}
        </span>
        <h1 className="text-3xl font-bold tracking-tight">{lesson.title}</h1>
        <p className="mt-1 text-sm text-slate-500">~{lesson.estimatedMinutes} min</p>
      </header>

      <ol className="space-y-3">
        {STEPS.map((step, i) => (
          <li
            key={step.key}
            className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm flex items-center justify-between"
          >
            <div>
              <span className="text-xs font-mono text-slate-400">Step {i + 1}</span>
              <p className="font-semibold">{step.label}</p>
              <p className="text-xs text-slate-500">~{step.minutes} min</p>
            </div>
            <span className="text-xs text-slate-400">
              {lesson.progress?.[(step.key + 'Completed') as keyof typeof lesson.progress]
                ? '✓'
                : '·'}
            </span>
          </li>
        ))}
      </ol>

      <article className="prose prose-slate max-w-none rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        {/* Markdown rendering is intentionally simple here — body comes already-rendered or plain text */}
        <pre className="whitespace-pre-wrap font-sans">{lesson.bodyMarkdownEn}</pre>
      </article>

      <Link href="/listening/lessons" className="text-sm text-blue-700 underline">
        ← All lessons
      </Link>
    </main>
  );
}
