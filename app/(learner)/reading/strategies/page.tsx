'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookOpen, CheckCircle2, Clock } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { getStrategies, type ReadingStrategyDto } from '@/lib/reading-pathway-api';

// ─── Filter chips ─────────────────────────────────────────────────────────────

const CATEGORIES = ['All', 'Scanning', 'Inference', 'Time Management', 'Distractor', 'Exam Day'] as const;

type Category = (typeof CATEGORIES)[number];

// Map display category to API category value
function toApiCategory(cat: Category): string | undefined {
  return cat === 'All' ? undefined : cat;
}

// ─── Strategy card ────────────────────────────────────────────────────────────

function StrategyCard({ strategy }: { strategy: ReadingStrategyDto }) {
  return (
    <Link
      href={`/reading/strategies/${strategy.slug}`}
      className="block rounded-2xl border border-border bg-surface p-4 shadow-sm transition-[border-color,box-shadow] duration-200 hover:border-border-hover hover:shadow-clinical focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="flex-1 text-sm font-semibold leading-snug text-foreground">{strategy.title}</h3>
        {strategy.isRead ? (
          <CheckCircle2 className="h-4 w-4 shrink-0 text-success-strong" aria-label="Read" />
        ) : (
          <BookOpen className="h-4 w-4 shrink-0 text-muted" aria-label="Unread" />
        )}
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Badge variant="info">{strategy.category}</Badge>
        <Badge variant={strategy.difficulty === 'Advanced' ? 'warning' : 'default'}>
          {strategy.difficulty}
        </Badge>
        <span className="flex items-center gap-1 text-xs text-muted">
          <Clock className="h-3 w-3" aria-hidden />
          {strategy.estimatedReadMinutes} min
        </span>
        {strategy.isRead && (
          <span className="text-xs font-medium text-emerald-600 dark:text-emerald-400">Read</span>
        )}
      </div>
    </Link>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function StrategiesPage() {
  const [strategies, setStrategies] = useState<ReadingStrategyDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeCategory, setActiveCategory] = useState<Category>('All');

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await getStrategies();
        if (!cancelled) setStrategies(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load strategies.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const filtered = useMemo(() => {
    const cat = toApiCategory(activeCategory);
    if (!cat) return strategies;
    return strategies.filter((s) => s.category === cat);
  }, [strategies, activeCategory]);

  return (
    <>
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow="Reading strategies"
          icon={BookOpen}
          accent="blue"
          title="Strategy Library"
          description="Evidence-based reading strategies to improve accuracy and speed in OET Part A, B, and C."
        />

        {/* Filter chips */}
        <div className="flex flex-wrap gap-2" role="group" aria-label="Filter by category">
          {CATEGORIES.map((cat) => (
            <button
              key={cat}
              type="button"
              aria-pressed={activeCategory === cat}
              onClick={() => setActiveCategory(cat)}
              className={cn(
                'min-h-9 rounded-full border px-4 py-1.5 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                activeCategory === cat
                  ? 'border-primary bg-primary text-white dark:border-primary-600 dark:bg-primary-700'
                  : 'border-border bg-surface text-muted hover:border-primary/40 hover:bg-primary/5',
              )}
            >
              {cat}
            </button>
          ))}
        </div>

        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} className="h-32 w-full rounded-2xl" />
            ))}
          </div>
        ) : filtered.length === 0 ? (
          <EmptyState
            icon={<BookOpen className="h-8 w-8" />}
            title="No strategies found for this category."
            action={activeCategory === 'All' ? undefined : { label: 'Show all strategies', onClick: () => setActiveCategory('All') }}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {filtered.map((strategy) => (
              <StrategyCard key={strategy.id} strategy={strategy} />
            ))}
          </div>
        )}
      </div>
    </>
  );
}
