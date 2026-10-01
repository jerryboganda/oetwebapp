'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { CardSkeleton } from '@/components/ui/skeleton';
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
    <div className="mx-auto max-w-5xl space-y-5 sm:space-y-8">
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
            type="button"
            aria-pressed={category === c.value}
            onClick={() => setCategory(c.value)}
            className={
              category === c.value
                ? 'min-h-9 rounded-full border border-primary bg-primary px-3.5 py-1.5 text-xs font-semibold text-white transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 dark:bg-primary-700'
                : 'min-h-9 rounded-full border border-border bg-surface px-3.5 py-1.5 text-xs font-semibold text-navy transition-colors hover:border-border-hover hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2'
            }
          >
            {c.label}
          </button>
        ))}
      </div>

      {isPending ? (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, i) => <CardSkeleton key={i} />)}
        </div>
      ) : isError ? (
        <ErrorState
          message="We couldn't load the strategy library."
          onRetry={() => void refetch()}
        />
      ) : strategies.length === 0 ? (
        <EmptyState
          title="No strategies published for this category yet."
          description="Try another category, or browse the full library."
          action={category ? { label: 'Show all strategies', onClick: () => setCategory('') } : undefined}
        />
      ) : (
        <ul className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {strategies.map((s) => (
            <li
              key={s.id}
              className="rounded-2xl border border-border bg-surface p-5 shadow-sm flex flex-col"
            >
              <span className="eyebrow text-muted">{s.category.replace('_', ' ')}</span>
              <h2 className="mt-1 font-semibold text-navy">{s.title}</h2>
              <p className="mt-1 text-xs text-muted">~{s.estimatedReadMinutes} min read</p>
              <div className="mt-2 flex items-center gap-2 text-xs">
                {s.markedAsRead && (
                  <span className="rounded-full bg-success/10 px-2 py-0.5 font-semibold text-success-strong">Read</span>
                )}
                {s.favorited && <span role="img" aria-label="Favorited">⭐</span>}
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
    </div>
  );
}
