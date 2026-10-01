'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Check, Lightbulb } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
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
      <div aria-busy="true" className="learner-page-flow">
        <p className="sr-only">Loading strategy…</p>
        <Skeleton className="h-32 rounded-2xl" />
        <CardSkeleton />
      </div>
    );
  }

  if (!strategy) {
    return (
      <EmptyState
        icon={<Lightbulb className="h-8 w-8" aria-hidden />}
        title="Strategy not found"
        action={{ label: 'Back to library', href: '/listening/strategies' }}
      />
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow={strategy.category.replace('_', ' ')}
        icon={Lightbulb}
        accent="purple"
        title={strategy.title}
        description={`~${strategy.estimatedReadMinutes} min read`}
      />

      <MotionSection>
        <Card padding="lg">
          {/* Long-form text: cap the line length, not the page. */}
          <MarkdownContent markdown={strategy.bodyMarkdownEn} className="max-w-prose text-navy" />
        </Card>
      </MotionSection>

      <div className="flex flex-wrap items-center gap-3">
        <Button
          size="sm"
          onClick={markRead}
          disabled={strategy.progress?.markedAsRead}
        >
          {strategy.progress?.markedAsRead ? (
            <>
              <Check className="h-4 w-4" aria-hidden />
              Marked as read
            </>
          ) : 'Mark as read'}
        </Button>
        <Button
          size="sm"
          variant="outline"
          aria-pressed={strategy.progress?.favorited ?? false}
          onClick={toggleFavorite}
        >
          {strategy.progress?.favorited ? '⭐ Favorited' : '☆ Favorite'}
        </Button>
        <Link
          href="/listening/strategies"
          className="inline-flex min-h-11 items-center gap-1.5 rounded-control text-sm font-medium text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:ms-auto"
        >
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden />
          All strategies
        </Link>
      </div>
    </>
  );
}
