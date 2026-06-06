'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { apiClient } from '@/lib/api';
import { ErrorState } from '@/components/ui/empty-error';

interface LessonItem {
  id: string;
  slug: string;
  title: string;
  skillCode: string;
  orderIndex: number;
  estimatedMinutes: number;
  isPublished: boolean;
  completedByUser: boolean;
}

const SKILL_LABEL: Record<string, string> = {
  L1: 'Detail capture',
  L2: 'Note-taking speed',
  L3: 'Spelling accuracy',
  L4: 'Gist comprehension',
  L5: 'Distractor recognition',
  L6: 'Inference',
  L7: 'Speaker stance',
  L8: 'Accent adaptation',
};

export default function ListeningLessonsPage() {
  const [lessons, setLessons] = useState<LessonItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(false);
    apiClient.get<LessonItem[]>('/v1/listening-pathway/lessons')
      .then((data: LessonItem[]) => {
        if (!cancelled) {
          setLessons(data);
          setLoading(false);
        }
      })
      .catch(() => {
        // FE-021: surface a retryable error instead of silently falling through
        // to the empty "being seeded" state on a failed fetch.
        if (!cancelled) {
          setError(true);
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return (
    <main className="mx-auto max-w-5xl px-4 py-12 space-y-8">
      <header>
        <h1 className="text-3xl font-bold tracking-tight text-navy">Foundation Lessons</h1>
        <p className="mt-2 text-muted">
          8 bite-sized lessons covering each Listening sub-skill (L1–L8). Each lesson runs ~30 min:
          watch, read, drill × 3, then a mini-quiz.
        </p>
      </header>

      {loading ? (
        <p className="text-muted">Loading lessons…</p>
      ) : error ? (
        <ErrorState
          message="We couldn't load the foundation lessons."
          onRetry={() => setReloadKey((k) => k + 1)}
        />
      ) : lessons.length === 0 ? (
        <div className="rounded-2xl border border-border bg-surface p-6 text-muted">
          <p>Foundation lessons are being seeded by the content team.</p>
          <p className="mt-2 text-sm">
            Enable the <code className="rounded bg-background-light px-1">Seed:ListeningContent</code> flag
            in development to populate sample lessons.
          </p>
        </div>
      ) : (
        <ol className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          {lessons.map((l) => (
            <li
              key={l.id}
              className="rounded-2xl border border-border bg-surface p-5 shadow-sm flex flex-col"
            >
              <span className="text-xs font-semibold tracking-wide text-primary uppercase">
                {l.skillCode} · {SKILL_LABEL[l.skillCode] ?? l.skillCode}
              </span>
              <h2 className="mt-1 text-base font-semibold text-navy">{l.title}</h2>
              <p className="mt-1 text-xs text-muted">~{l.estimatedMinutes} min</p>
              {l.completedByUser && (
                <span className="mt-2 inline-block rounded-full bg-success/10 px-2 py-0.5 text-xs text-success">
                  ✓ Completed
                </span>
              )}
              <Link
                href={`/listening/lessons/${l.slug}`}
                className="mt-4 self-start text-sm text-primary underline"
              >
                Open lesson →
              </Link>
            </li>
          ))}
        </ol>
      )}
    </main>
  );
}
