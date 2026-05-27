'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';

interface Strategy {
  id: string;
  slug: string;
  title: string;
  category: string;
  estimatedReadMinutes: number;
  difficulty: number;
  markedAsRead: boolean;
  favorited: boolean;
}

const CATEGORIES = [
  { value: '', label: 'All' },
  { value: 'note_taking', label: 'Note-taking' },
  { value: 'gist', label: 'Gist' },
  { value: 'inference', label: 'Inference' },
  { value: 'time_management', label: 'Time management' },
  { value: 'accent', label: 'Accent handling' },
  { value: 'exam_day', label: 'Exam day' },
];

export default function ListeningStrategiesPage() {
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [category, setCategory] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    const url = category
      ? `/v1/listening-pathway/strategies?category=${encodeURIComponent(category)}`
      : '/v1/listening-pathway/strategies';
    setLoading(true);
    fetch(url)
      .then((res) => (res.ok ? res.json() : []))
      .then((d: Strategy[]) => {
        if (!cancelled) {
          setStrategies(d);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [category]);

  return (
    <main className="mx-auto max-w-5xl px-4 py-12 space-y-8">
      <header>
        <h1 className="text-3xl font-bold tracking-tight">Strategy Library</h1>
        <p className="mt-2 text-slate-600">
          Curated tactics for note-taking, gist, inference, time management, accents, and exam day.
        </p>
      </header>

      <div className="flex flex-wrap gap-2">
        {CATEGORIES.map((c) => (
          <button
            key={c.value}
            onClick={() => setCategory(c.value)}
            className={
              category === c.value
                ? 'rounded-full bg-slate-900 px-3 py-1 text-xs text-white'
                : 'rounded-full border border-slate-300 px-3 py-1 text-xs text-slate-700'
            }
          >
            {c.label}
          </button>
        ))}
      </div>

      {loading ? (
        <p className="text-slate-500">Loading strategies…</p>
      ) : strategies.length === 0 ? (
        <div className="rounded-2xl border border-slate-200 bg-white p-6 text-slate-600">
          No strategies published for this category yet.
        </div>
      ) : (
        <ul className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {strategies.map((s) => (
            <li
              key={s.id}
              className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm flex flex-col"
            >
              <span className="text-xs uppercase tracking-wide text-slate-500">{s.category.replace('_', ' ')}</span>
              <h2 className="mt-1 font-semibold">{s.title}</h2>
              <p className="mt-1 text-xs text-slate-500">~{s.estimatedReadMinutes} min read</p>
              <div className="mt-2 flex items-center gap-2 text-xs">
                {s.markedAsRead && (
                  <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-emerald-700">Read</span>
                )}
                {s.favorited && <span>⭐</span>}
              </div>
              <Link
                href={`/listening/strategies/${s.slug}`}
                className="mt-3 self-start text-sm text-blue-700 underline"
              >
                Open →
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
