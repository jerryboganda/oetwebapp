'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  CalendarDays,
  ChevronRight,
  Trophy,
  Sparkles,
} from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { listeningV2Api, type ListeningPathwayStageView } from '@/lib/listening/v2-api';

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function statusStyle(status: ListeningPathwayStageView['status']): {
  badge: string;
  border: string;
  bg: string;
  text: string;
} {
  switch (status) {
    case 'Completed':
      return {
        badge: 'bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200 dark:bg-emerald-950/50 dark:text-emerald-300 dark:ring-emerald-800/60',
        border: 'border-success/30',
        bg: 'bg-success/5',
        text: 'text-emerald-900 dark:text-emerald-200',
      };
    case 'InProgress':
      return {
        badge: 'bg-amber-100 text-amber-800 ring-1 ring-amber-200 dark:bg-amber-950/50 dark:text-amber-300 dark:ring-amber-800/60',
        border: 'border-warning/30',
        bg: 'bg-warning/5',
        text: 'text-amber-900 dark:text-amber-200',
      };
    case 'Unlocked':
      return {
        badge: 'bg-sky-100 text-sky-800 ring-1 ring-sky-200 dark:bg-sky-950/50 dark:text-sky-300 dark:ring-sky-800/60',
        border: 'border-info/30',
        bg: 'bg-info/5',
        text: 'text-sky-900 dark:text-sky-200',
      };
    default:
      return {
        badge: 'bg-background-light text-muted ring-1 ring-border',
        border: 'border-border',
        bg: 'bg-background-light/60',
        text: 'text-muted',
      };
  }
}

function stageLabel(stage: string) {
  return stage
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function StageCard({ stage, index }: { stage: ListeningPathwayStageView; index: number }) {
  const style = statusStyle(stage.status);
  return (
    <article
      className={`flex flex-col gap-3 rounded-2xl border ${style.border} ${style.bg} p-5 shadow-sm`}
      aria-label={`Stage ${index + 1}, ${stageLabel(stage.stage)}`}
    >
      <header className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-extrabold text-navy">Stage {index + 1}</h3>
        <span
          className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-3xs font-bold uppercase tracking-wide ${style.badge}`}
        >
          {stage.status}
        </span>
      </header>

      <p className={`text-sm font-semibold ${style.text}`}>{stageLabel(stage.stage)}</p>

      <div className="mt-auto flex items-center justify-between border-t border-border pt-3 text-2xs text-muted">
        <span className="font-semibold">
          {stage.scaledScore === null ? 'No score yet' : `${stage.scaledScore}/500`}
        </span>
        {stage.completedAt ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-success/10 px-2 py-0.5 font-bold text-emerald-800 dark:text-emerald-300">
            <Trophy className="h-3 w-3" aria-hidden />
            Complete
          </span>
        ) : (
          <span className="text-muted">Pending</span>
        )}
      </div>

      {stage.actionHref ? (
        <Button asChild size="sm" variant="outline" className="self-start bg-surface">
          <Link href={stage.actionHref}>
            Continue
            <ChevronRight className="h-3.5 w-3.5" aria-hidden />
          </Link>
        </Button>
      ) : null}
    </article>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Page
// ─────────────────────────────────────────────────────────────────────────────

export default function ListeningPathwayPage() {
  const [pathway, setPathway] = useState<ListeningPathwayStageView[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await listeningV2Api.myPathway();
        if (!cancelled) setPathway(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load pathway.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const mockCount = useMemo(
    () => pathway?.filter((stage) => stage.completedAt).length ?? 0,
    [pathway],
  );

  return (
    <>
      <div className="mx-auto max-w-5xl space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow="Listening pathway"
          icon={CalendarDays}
          title="Your listening roadmap"
          description="A personalised schedule of focus skills, accent practice, and mock tests."
        />

        {pathway && (
          <div className="flex flex-wrap gap-6 rounded-2xl border border-border bg-surface p-5 text-sm shadow-sm">
            <div>
              <span className="block eyebrow text-muted">
                Total stages
              </span>
              <span className="text-xl font-bold text-primary">{pathway.length}</span>
            </div>
            <div>
              <span className="block eyebrow text-muted">
                Completed
              </span>
              <span className="text-xl font-bold text-navy">{mockCount}</span>
            </div>
            <div className="flex items-center gap-2">
              <CalendarDays className="h-4 w-4 text-muted" aria-hidden />
              <span className="text-sm text-muted">
                Server-authoritative V2 pathway
              </span>
            </div>
          </div>
        )}

        {error ? (
          <div className="space-y-4">
            <InlineAlert variant="error">{error}</InlineAlert>
            <Link
              href="/listening"
              className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            >
              Back to Listening
              <ChevronRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        ) : loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-44 w-full rounded-2xl" />
            ))}
          </div>
        ) : pathway && pathway.length > 0 ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {pathway.map((stage, index) => (
              <StageCard key={stage.stage} stage={stage} index={index} />
            ))}
          </div>
        ) : (
          <div className="rounded-2xl border border-dashed border-border bg-background-light px-6 py-10 text-center shadow-sm">
            <Sparkles className="mx-auto mb-3 h-8 w-8 text-muted" aria-hidden />
            <p className="font-semibold text-navy">No pathway generated yet</p>
            <p className="mt-1 text-sm text-muted">
              Your personalised 12-week plan is being prepared. Start practising in the meantime.
            </p>
            <Button asChild size="sm" className="mt-4">
              <Link href="/listening">
                Start practising
                <ChevronRight className="h-4 w-4" aria-hidden />
              </Link>
            </Button>
          </div>
        )}
      </div>
    </>
  );
}
