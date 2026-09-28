'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Button } from '@/components/ui/button';
import { CardSkeleton, Skeleton } from '@/components/ui/skeleton';
import { apiClient } from '@/lib/api';

interface StrategyDetail {
  id: string;
  slug: string;
  title: string;
  category: string;
  applicableParts: string[];
  estimatedReadMinutes: number;
  bodyMarkdownEn: string;
  videoUrl: string | null;
  audioUrl: string | null;
  progress?: {
    markedAsRead: boolean;
    favorited: boolean;
  };
}

export default function ListeningStrategyDetailPage() {
  const params = useParams<{ slug: string }>();
  const slug = params?.slug ?? '';
  const [strategy, setStrategy] = useState<StrategyDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient.get<StrategyDetail | null>(`/v1/listening-pathway/strategies/${encodeURIComponent(slug)}`)
      .then((d: StrategyDetail | null) => {
        if (!cancelled) {
          setStrategy(d);
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

  async function markRead() {
    if (!strategy) return;
    await apiClient.post(`/v1/listening-pathway/strategies/${strategy.id}/mark-read`);
    setStrategy({ ...strategy, progress: { markedAsRead: true, favorited: strategy.progress?.favorited ?? false } });
  }

  async function toggleFavorite() {
    if (!strategy) return;
    await apiClient.post(`/v1/listening-pathway/strategies/${strategy.id}/favorite`);
    setStrategy({
      ...strategy,
      progress: {
        markedAsRead: strategy.progress?.markedAsRead ?? false,
        favorited: !strategy.progress?.favorited,
      },
    });
  }

  if (loading) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-6" aria-busy="true">
        <p className="sr-only">Loading strategy…</p>
        <Skeleton className="h-9 w-2/3 rounded-lg" />
        <CardSkeleton />
      </main>
    );
  }

  if (!strategy) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold text-navy">Strategy not found</h1>
        <Button asChild size="sm">
          <Link href="/listening/strategies">Back to library</Link>
        </Button>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <header>
        <span className="text-xs uppercase tracking-wide text-muted">
          {strategy.category.replace('_', ' ')}
        </span>
        <h1 className="text-3xl font-bold tracking-tight text-navy">{strategy.title}</h1>
        <p className="mt-1 text-sm text-muted">~{strategy.estimatedReadMinutes} min read</p>
      </header>

      <MarkdownContent
        markdown={strategy.bodyMarkdownEn}
        className="rounded-2xl border border-border bg-surface p-6 shadow-sm text-navy"
      />

      <div className="flex flex-wrap gap-3">
        <Button
          size="sm"
          onClick={markRead}
          disabled={strategy.progress?.markedAsRead}
        >
          {strategy.progress?.markedAsRead ? '✓ Marked as read' : 'Mark as read'}
        </Button>
        <Button
          size="sm"
          variant="outline"
          aria-pressed={strategy.progress?.favorited ?? false}
          onClick={toggleFavorite}
        >
          {strategy.progress?.favorited ? '⭐ Favorited' : '☆ Favorite'}
        </Button>
      </div>

      <Link href="/listening/strategies" className="text-sm text-primary underline transition-colors hover:text-primary-dark">
        ← All strategies
      </Link>
    </main>
  );
}
