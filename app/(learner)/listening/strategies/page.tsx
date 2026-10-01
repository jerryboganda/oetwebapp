'use client';

import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Lightbulb } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { CardSkeleton } from '@/components/ui/skeleton';
import { queryKeys } from '@/lib/query/hooks';
import { cn } from '@/lib/utils';

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
    <>
      <LearnerPageHero
        icon={Lightbulb}
        accent="purple"
        title="Strategy Library"
        description="Curated tactics for note-taking, gist, inference, time management, accents, and exam day."
      />

      <div className="flex flex-wrap gap-2">
        {CATEGORIES.map((c) => (
          <button
            key={c.value}
            type="button"
            aria-pressed={category === c.value}
            onClick={() => setCategory(c.value)}
            className={cn(
              'min-h-11 rounded-full border px-4 py-2 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
              category === c.value
                ? 'border-primary bg-primary text-white dark:bg-primary-700'
                : 'hover-primary border-border bg-surface text-navy',
            )}
          >
            {c.label}
          </button>
        ))}
      </div>

      {isPending ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, i) => <CardSkeleton key={i} />)}
        </div>
      ) : isError ? (
        <ErrorState
          message="We couldn't load the strategy library."
          onRetry={() => void refetch()}
        />
      ) : strategies.length === 0 ? (
        <EmptyState
          icon={<Lightbulb className="h-8 w-8" aria-hidden />}
          title="No strategies published for this category yet."
          description="Try another category, or browse the full library."
          action={category ? { label: 'Show all strategies', onClick: () => setCategory('') } : undefined}
        />
      ) : (
        <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {strategies.map((s, index) => (
            <li key={s.id}>
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                {/* The whole card opens the strategy. */}
                <CardLink href={`/listening/strategies/${s.slug}`} className="group flex h-full flex-col">
                  <span className="eyebrow text-muted">{s.category.replace('_', ' ')}</span>
                  <h2 className="mt-1 font-semibold text-navy">{s.title}</h2>
                  <p className="mt-1 text-xs tabular-nums text-muted">~{s.estimatedReadMinutes} min read</p>
                  <div className="mt-auto flex items-center gap-2 pt-3">
                    {s.markedAsRead ? <Badge variant="success">Read</Badge> : null}
                    {s.favorited ? <span role="img" aria-label="Favorited">⭐</span> : null}
                    <ArrowRight className="ms-auto h-4 w-4 text-primary transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden />
                  </div>
                </CardLink>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
