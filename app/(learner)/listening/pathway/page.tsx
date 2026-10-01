'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  CalendarDays,
  CheckCircle2,
  ChevronRight,
  ListChecks,
  Trophy,
  Sparkles,
} from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { listeningV2Api, type ListeningPathwayStageView } from '@/lib/listening/v2-api';
import { cn } from '@/lib/utils';

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

// Each stage status keeps its colour; the Badge carries the word, so status is
// never colour alone.
function statusStyle(status: ListeningPathwayStageView['status']): {
  badge: 'success' | 'warning' | 'info' | 'muted';
  card: string;
  text: string;
} {
  switch (status) {
    case 'Completed':
      return { badge: 'success', card: 'border-success/30 bg-success/5', text: 'text-success-strong' };
    case 'InProgress':
      return { badge: 'warning', card: 'border-warning/30 bg-warning/5', text: 'text-warning-strong' };
    case 'Unlocked':
      return { badge: 'info', card: 'border-info/30 bg-info/5', text: 'text-info' };
    default:
      return { badge: 'muted', card: 'bg-background-light/60', text: 'text-muted' };
  }
}

function stageLabel(stage: string) {
  return stage
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

/** "InProgress" → "In Progress": the status word as a learner reads it. */
function statusLabel(status: string) {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function StageCard({ stage, index }: { stage: ListeningPathwayStageView; index: number }) {
  const style = statusStyle(stage.status);
  return (
    <article
      className={cn(cardClassName({}), 'flex h-full flex-col gap-3', style.card)}
      aria-label={`Stage ${index + 1}, ${stageLabel(stage.stage)}`}
    >
      <header className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-bold tabular-nums text-navy">Stage {index + 1}</h3>
        <Badge variant={style.badge}>{statusLabel(stage.status)}</Badge>
      </header>

      <p className={`text-sm font-semibold ${style.text}`}>{stageLabel(stage.stage)}</p>

      <div className="mt-auto flex items-center justify-between border-t border-border pt-3 text-2xs text-muted">
        <span className="font-semibold tabular-nums">
          {stage.scaledScore === null ? 'No score yet' : `${stage.scaledScore}/500`}
        </span>
        {stage.completedAt ? (
          <Badge variant="success" className="gap-1">
            <Trophy className="h-3 w-3" aria-hidden />
            Complete
          </Badge>
        ) : (
          <span className="text-muted">Pending</span>
        )}
      </div>

      {stage.actionHref ? (
        <Button asChild size="sm" variant="outline" className="self-start bg-surface">
          <Link href={stage.actionHref}>
            Continue
            <ChevronRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden />
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

  // The stage counts are the hero's chips (they were a separate strip, next to
  // an implementation note, "Server-authoritative V2 pathway", not meant for learners).
  return (
    <>
      <LearnerPageHero
        eyebrow="Listening pathway"
        icon={CalendarDays}
        accent="purple"
        title="Your listening roadmap"
        description="A personalised schedule of focus skills, accent practice, and mock tests."
        highlights={pathway ? [
          { icon: ListChecks, label: 'Total stages', value: String(pathway.length) },
          { icon: CheckCircle2, label: 'Completed', value: String(mockCount) },
        ] : undefined}
      />

      {error ? (
        <ErrorState message={error} />
      ) : loading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-44 w-full rounded-2xl" />
          ))}
        </div>
      ) : pathway && pathway.length > 0 ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {pathway.map((stage, index) => (
            <MotionItem key={stage.stage} delayIndex={Math.min(index, 5)} className="h-full">
              <StageCard stage={stage} index={index} />
            </MotionItem>
          ))}
        </div>
      ) : (
        <EmptyState
          icon={<Sparkles className="h-8 w-8" aria-hidden />}
          title="No pathway generated yet"
          description="Your personalised 12-week plan is being prepared. Start practising in the meantime."
          action={{ label: 'Start practising', href: '/listening' }}
        />
      )}
    </>
  );
}
