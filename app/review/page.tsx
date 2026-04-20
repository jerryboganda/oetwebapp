'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { Brain, CheckCircle2, Clock, PlayCircle, RotateCcw, Sparkles } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout';
import {
  LearnerPageHero,
  LearnerSurfaceCard,
  LearnerSurfaceSectionHeader,
} from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  RetentionTrendCard,
  ReviewHeatmapCard,
  ReviewEmptyState,
  ReviewSessionModal,
  UpcomingReviewRail,
  type SessionSummary,
} from '@/components/domain/review';
import {
  fetchDueReviewItems,
  fetchReviewHeatmap,
  fetchReviewRetention,
  fetchReviewSummary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type {
  ReviewHeatmapResponse,
  ReviewItem,
  ReviewRetentionResponse,
  ReviewSummary,
} from '@/lib/types/review';

type LoadState = 'loading' | 'ready' | 'error';

interface PageData {
  summary: ReviewSummary | null;
  items: ReviewItem[];
  retention: ReviewRetentionResponse | null;
  heatmap: ReviewHeatmapResponse | null;
}

const EMPTY_DATA: PageData = {
  summary: null,
  items: [],
  retention: null,
  heatmap: null,
};

function normaliseItems(payload: unknown): ReviewItem[] {
  if (Array.isArray(payload)) return payload as ReviewItem[];
  if (payload && typeof payload === 'object' && 'items' in payload) {
    const items = (payload as { items?: unknown }).items;
    if (Array.isArray(items)) return items as ReviewItem[];
  }
  return [];
}

export default function ReviewPage() {
  const searchParams = useSearchParams();
  const autoStart = searchParams?.get('session') === 'start';

  const [data, setData] = useState<PageData>(EMPTY_DATA);
  const [status, setStatus] = useState<LoadState>('loading');
  const [error, setError] = useState<string | null>(null);
  const [sessionOpen, setSessionOpen] = useState(false);
  const [lastSessionSummary, setLastSessionSummary] = useState<SessionSummary | null>(null);

  const loadAll = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [summaryR, itemsR, retentionR, heatmapR] = await Promise.allSettled([
        fetchReviewSummary(),
        fetchDueReviewItems(20, { includeVocabulary: true }),
        fetchReviewRetention(30),
        fetchReviewHeatmap(),
      ]);

      const summary = summaryR.status === 'fulfilled' ? (summaryR.value as ReviewSummary) : null;
      const items = itemsR.status === 'fulfilled' ? normaliseItems(itemsR.value) : [];
      const retention =
        retentionR.status === 'fulfilled' ? (retentionR.value as ReviewRetentionResponse) : null;
      const heatmap =
        heatmapR.status === 'fulfilled' ? (heatmapR.value as ReviewHeatmapResponse) : null;

      setData({ summary, items, retention, heatmap });

      if (summaryR.status === 'rejected' && itemsR.status === 'rejected') {
        setError('Could not load your review queue. Please try again.');
        setStatus('error');
      } else {
        setStatus('ready');
      }
    } catch (err) {
      console.error('Review load failed', err);
      setError('Could not load your review queue. Please try again.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    analytics.track('review_page_viewed');
    // Defer first load out of render-phase to avoid synchronous cascading setState.
    const timer = setTimeout(() => {
      void loadAll();
    }, 0);
    return () => clearTimeout(timer);
  }, [loadAll]);

  useEffect(() => {
    if (autoStart && status === 'ready' && data.items.length > 0 && !sessionOpen) {
      // Schedule the open on the next tick so we never mutate state within
      // the same render cycle the effect was scheduled in.
      const timer = setTimeout(() => setSessionOpen(true), 0);
      return () => clearTimeout(timer);
    }
    return undefined;
  }, [autoStart, status, data.items.length, sessionOpen]);

  const handleSessionComplete = useCallback(
    (summary: SessionSummary) => {
      setLastSessionSummary(summary);
      // Silently refresh counters after the session finishes.
      void loadAll();
    },
    [loadAll],
  );

  const heroHighlights = useMemo(
    () => [
      {
        icon: Brain,
        label: 'Due today',
        value: String(data.summary?.dueToday ?? 0),
      },
      {
        icon: CheckCircle2,
        label: 'Mastered',
        value: String(data.summary?.mastered ?? 0),
      },
      {
        icon: RotateCcw,
        label: 'Mode',
        value: 'Spaced review',
      },
    ],
    [data.summary],
  );

  const dueCount = data.summary?.due ?? data.items.length ?? 0;
  const totalItems = data.summary?.total ?? 0;
  const hasQueue = data.items.length > 0;
  const hasAnyItems = totalItems > 0;

  const startCard: LearnerSurfaceCardModel = useMemo(
    () => ({
      kind: 'task',
      sourceType: 'frontend_insight',
      accent: 'primary',
      eyebrow: hasQueue ? 'Recommended Next' : 'Queue empty',
      eyebrowIcon: Sparkles,
      title: hasQueue ? "Today's review session" : "You're caught up for today",
      description: hasQueue
        ? 'Work through a focused session of spaced repetition items. Ratings update the schedule instantly so your weak areas come back at the right time.'
        : 'Nothing is due right now. Complete a practice task or save vocabulary to keep your retention queue moving.',
      metaItems: [
        { icon: Clock, label: `${dueCount} due` },
        { icon: Brain, label: `${data.summary?.mastered ?? 0} mastered` },
      ],
      primaryAction: hasQueue
        ? {
            label: `Start review (${dueCount})`,
            onClick: () => setSessionOpen(true),
            variant: 'primary',
          }
        : {
            label: 'View practice modules',
            href: '/study-plan',
            variant: 'outline',
          },
    }),
    [hasQueue, dueCount, data.summary?.mastered],
  );

  const heatmapCard: LearnerSurfaceCardModel = useMemo(
    () => ({
      kind: 'evidence',
      sourceType: 'backend_summary',
      accent: 'navy',
      eyebrow: 'Coverage',
      eyebrowIcon: PlayCircle,
      title: 'Where your reviews are coming from',
      description:
        'The engine seeds cards automatically across writing, speaking, reading, listening, grammar, pronunciation, vocabulary, and mocks. This card summarises the split.',
      metaItems: [
        { icon: Brain, label: `${totalItems} total items` },
        { icon: Clock, label: `${data.summary?.upcoming ?? 0} upcoming 7d` },
      ],
      secondaryAction: {
        label: 'Open study plan',
        href: '/study-plan',
        variant: 'outline',
      },
    }),
    [totalItems, data.summary?.upcoming],
  );

  return (
    <LearnerDashboardShell pageTitle="Review">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Learn"
          title="Spaced Repetition Review"
          description="Every mistake you make across the platform becomes a review card. Rate them here and the engine schedules the next session."
          icon={Brain}
          highlights={heroHighlights}
        />

        {error ? (
          <InlineAlert variant="warning">{error}</InlineAlert>
        ) : null}

        {lastSessionSummary ? (
          <InlineAlert variant="info">
            Last session: {lastSessionSummary.reviewed} reviewed · {lastSessionSummary.correct} correct
            {lastSessionSummary.masteredJustNow > 0
              ? ` · +${lastSessionSummary.masteredJustNow} mastered`
              : null}
          </InlineAlert>
        ) : null}

        {/* Two-card hero grid — mirrors app/page.tsx rhythm */}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <MotionItem delayIndex={0}>
            <LearnerSurfaceCard card={startCard} />
          </MotionItem>
          <MotionItem delayIndex={1}>
            <LearnerSurfaceCard card={heatmapCard} />
          </MotionItem>
        </div>

        {status === 'loading' && !data.summary ? (
          <SummarySkeleton />
        ) : (
          <SummaryStats summary={data.summary} />
        )}

        {/* Retention + upcoming rail row */}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          <div className="lg:col-span-8">
            <RetentionTrendCard data={data.retention} loading={status === 'loading'} />
          </div>
          <div className="lg:col-span-4">
            <UpcomingReviewRail items={data.items} loading={status === 'loading'} />
          </div>
        </div>

        {/* Empty / heatmap */}
        {status === 'ready' && !hasAnyItems ? (
          <ReviewEmptyState />
        ) : (
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Session overview"
              title="Weak area heatmap"
              description="The count of active review cards grouped by source. Focus first where the due count is highest."
              className="mb-4"
            />
            <ReviewHeatmapCard data={data.heatmap} loading={status === 'loading'} />
          </div>
        )}

        {/* Queue ready CTA - redundant secondary entry for small screens */}
        {hasQueue ? (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-3xl border border-border bg-surface px-5 py-4 shadow-sm">
            <div>
              <p className="text-sm font-semibold text-navy">Ready when you are</p>
              <p className="text-xs text-muted">
                {dueCount} card{dueCount === 1 ? '' : 's'} due · estimated {Math.max(1, Math.round(dueCount * 0.5))} minutes
              </p>
            </div>
            <button
              type="button"
              onClick={() => setSessionOpen(true)}
              className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary/90"
            >
              Start review
              <PlayCircle className="h-4 w-4" />
            </button>
          </div>
        ) : null}
      </div>

      <ReviewSessionModal
        open={sessionOpen}
        items={data.items}
        onClose={() => setSessionOpen(false)}
        onSessionComplete={handleSessionComplete}
      />
    </LearnerDashboardShell>
  );
}

function SummaryStats({ summary }: { summary: ReviewSummary | null }) {
  const stats = [
    {
      label: 'Due Today',
      value: summary?.dueToday ?? 0,
      tone: 'text-danger',
    },
    {
      label: 'Total Due',
      value: summary?.due ?? 0,
      tone: 'text-warning',
    },
    {
      label: 'Total Items',
      value: summary?.total ?? 0,
      tone: 'text-info',
    },
    {
      label: 'Mastered',
      value: summary?.mastered ?? 0,
      tone: 'text-success',
    },
  ];
  return (
    <div>
      <LearnerSurfaceSectionHeader
        eyebrow="Session overview"
        title="Review at a glance"
        description="A quick read of your retention queue, aligned with the dashboard summary row."
        className="mb-4"
      />
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {stats.map((stat) => (
          <Card key={stat.label} className="rounded-3xl p-4 text-center shadow-sm">
            <div className={`text-3xl font-bold ${stat.tone}`}>{stat.value}</div>
            <div className="mt-1 text-sm text-muted">{stat.label}</div>
          </Card>
        ))}
      </div>
    </div>
  );
}

function SummarySkeleton() {
  return (
    <div>
      <Skeleton className="mb-4 h-12 w-64 rounded-xl" />
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-3xl" />
        ))}
      </div>
    </div>
  );
}
