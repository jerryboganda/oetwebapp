'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api';
import { ErrorState } from '@/components/ui/empty-error';
import { queryKeys } from '@/lib/query/hooks';

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
  const [category, setCategory] = useState('');

  // FE-006: TanStack Query keys on `category`, so changing the filter refetches
  // (and caches per category) — replacing the manual effect + reloadKey (FE-021).
  const { data: strategies = [], isPending, isError, refetch } = useQuery({
    queryKey: queryKeys.listening.strategies(category),
    queryFn: () => {
      const url = category
        ? `/v1/listening-pathway/strategies?category=${encodeURIComponent(category)}`
        : '/v1/listening-pathway/strategies';
      return apiClient.get<Strategy[]>(url);
    },
  });

  return (
    <main className="mx-auto max-w-5xl px-4 py-12 space-y-8">
      <header>
        <h1 className="text-3xl font-bold tracking-tight text-navy">Strategy Library</h1>
        <p className="mt-2 text-muted">
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
                ? 'rounded-full bg-primary px-3 py-1 text-xs text-white dark:bg-violet-700 transition-colors'
                : 'rounded-full border border-border px-3 py-1 text-xs text-navy transition-colors hover:bg-background-light'
            }
          >
            {c.label}
          </button>
        ))}
      </div>

      {isPending ? (
        <p className="text-muted">Loading strategies…</p>
      ) : isError ? (
        <ErrorState
          message="We couldn't load the strategy library."
          onRetry={() => void refetch()}
        />
      ) : strategies.length === 0 ? (
        <div className="rounded-2xl border border-border bg-surface p-6 text-muted">
          No strategies published for this category yet.
        </div>
      ) : (
        <ul className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {strategies.map((s) => (
            <li
              key={s.id}
              className="rounded-2xl border border-border bg-surface p-5 shadow-sm flex flex-col"
            >
              <span className="text-xs uppercase tracking-wide text-muted">{s.category.replace('_', ' ')}</span>
              <h2 className="mt-1 font-semibold text-navy">{s.title}</h2>
              <p className="mt-1 text-xs text-muted">~{s.estimatedReadMinutes} min read</p>
              <div className="mt-2 flex items-center gap-2 text-xs">
                {s.markedAsRead && (
                  <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-emerald-700">Read</span>
                )}
                {s.favorited && <span>⭐</span>}
              </div>
              <Link
                href={`/listening/strategies/${s.slug}`}
                className="mt-3 self-start text-sm text-primary underline transition-colors hover:text-primary-dark"
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
