'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  CalendarDays,
  ChevronRight,
  Trophy,
  Sparkles,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
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
        badge: 'bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200',
        border: 'border-emerald-200',
        bg: 'bg-emerald-50/40',
        text: 'text-emerald-900',
      };
    case 'InProgress':
      return {
        badge: 'bg-amber-100 text-amber-800 ring-1 ring-amber-200',
        border: 'border-amber-200',
        bg: 'bg-amber-50/40',
        text: 'text-amber-900',
      };
    case 'Unlocked':
      return {
        badge: 'bg-sky-100 text-sky-800 ring-1 ring-sky-200',
        border: 'border-sky-200',
        bg: 'bg-sky-50/50',
        text: 'text-sky-900',
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
          className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-wide ${style.badge}`}
        >
          {stage.status}
        </span>
      </header>

      <p className={`text-sm font-semibold ${style.text}`}>{stageLabel(stage.stage)}</p>

      <div className="mt-auto flex items-center justify-between border-t border-border pt-3 text-[11px] text-muted">
        <span className="font-semibold">
          {stage.scaledScore === null ? 'No score yet' : `${stage.scaledScore}/500`}
        </span>
        {stage.completedAt ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 font-bold text-emerald-800">
            <Trophy className="h-3 w-3" aria-hidden />
            Complete
          </span>
        ) : (
          <span className="text-muted">Pending</span>
        )}
      </div>

      {stage.actionHref ? (
        <Link
          href={stage.actionHref}
          className="inline-flex items-center gap-1 text-xs font-bold text-primary hover:underline"
        >
          Continue
          <ChevronRight className="h-3 w-3" aria-hidden />
        </Link>
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
    <LearnerDashboardShell pageTitle="Listening Pathway">
      <div className="mx-auto max-w-5xl space-y-8">
        <div>
          <h1 className="text-2xl font-bold text-navy">Your listening roadmap</h1>
          <p className="mt-1 text-sm text-muted">
            A personalised 12-week schedule of focus skills, accent practice, and mock tests.
          </p>
        </div>

        {pathway && (
          <div className="flex flex-wrap gap-6 rounded-2xl border border-border bg-surface p-5 text-sm">
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted">
                Total stages
              </span>
              <span className="text-xl font-bold text-primary">{pathway.length}</span>
            </div>
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted">
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
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <Sparkles className="mx-auto mb-3 h-8 w-8 text-muted" aria-hidden />
            <p className="font-semibold text-navy">No pathway generated yet</p>
            <p className="mt-1 text-sm text-muted">
              Complete the listening diagnostic to unlock your personalised 12-week plan.
            </p>
            <Link
              href="/listening/diagnostic"
              className="mt-4 inline-flex items-center gap-1 rounded-xl bg-primary px-4 py-2 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
            >
              Take the diagnostic
              <ChevronRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
