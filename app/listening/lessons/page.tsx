'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';

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

  useEffect(() => {
    let cancelled = false;
    fetch('/v1/listening-pathway/lessons')
      .then((res) => (res.ok ? res.json() : []))
      .then((data: LessonItem[]) => {
        if (!cancelled) {
          setLessons(data);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <main className="mx-auto max-w-5xl px-4 py-12 space-y-8">
      <header>
        <h1 className="text-3xl font-bold tracking-tight">Foundation Lessons</h1>
        <p className="mt-2 text-slate-600">
          8 bite-sized lessons covering each Listening sub-skill (L1–L8). Each lesson runs ~30 min:
          watch → read → drill × 3 → mini-quiz.
        </p>
      </header>

      {loading ? (
        <p className="text-slate-500">Loading lessons…</p>
      ) : lessons.length === 0 ? (
        <div className="rounded-2xl border border-slate-200 bg-white p-6 text-slate-600">
          <p>Foundation lessons are being seeded by the content team.</p>
          <p className="mt-2 text-sm">
            Enable the <code className="rounded bg-slate-100 px-1">Seed:ListeningContent</code> flag
            in development to populate sample lessons.
          </p>
        </div>
      ) : (
        <ol className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          {lessons.map((l) => (
            <li
              key={l.id}
              className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm flex flex-col"
            >
              <span className="text-xs font-semibold tracking-wide text-blue-700 uppercase">
                {l.skillCode} · {SKILL_LABEL[l.skillCode] ?? l.skillCode}
              </span>
              <h2 className="mt-1 text-base font-semibold">{l.title}</h2>
              <p className="mt-1 text-xs text-slate-500">~{l.estimatedMinutes} min</p>
              {l.completedByUser && (
                <span className="mt-2 inline-block rounded-full bg-emerald-100 px-2 py-0.5 text-xs text-emerald-700">
                  ✓ Completed
                </span>
              )}
              <Link
                href={`/listening/lessons/${l.slug}`}
                className="mt-4 self-start text-sm text-blue-700 underline"
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
