'use client';

import type { ElementType } from 'react';
import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  CheckCircle2,
  ChevronRight,
  Clock,
  FlaskConical,
  GraduationCap,
  Lock,
  Trophy,
  TrendingUp,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { getPathway, type PathwayDto } from '@/lib/reading-pathway-api';

// ─── Stage definitions ────────────────────────────────────────────────────────

interface StageConfig {
  key: string;
  number: number;
  icon: ElementType;
  name: string;
  description: string;
  link: string;
  ctaLabel: string;
}

const STAGES: StageConfig[] = [
  {
    key: 'onboarding',
    number: 1,
    icon: GraduationCap,
    name: 'Onboarding',
    description: 'Set your target band, exam date, and study hours. Unlocks your personalised reading plan.',
    link: '/reading',
    ctaLabel: 'Back to Reading home',
  },
  {
    key: 'diagnostic',
    number: 2,
    icon: FlaskConical,
    name: 'Diagnostic Assessment',
    description: 'A short adaptive test that maps your current strengths and weak spots across all 8 reading skills.',
    link: '/reading/diagnostic',
    ctaLabel: 'Start diagnostic',
  },
  {
    key: 'foundation',
    number: 3,
    icon: BookOpen,
    name: 'Sub-Skill Foundation',
    description: '8 targeted lessons covering scanning, skimming, paraphrase recognition, distractor patterns, inference, reference resolution, vocabulary, and time management.',
    link: '/reading/skill-tree',
    ctaLabel: 'View skill tree',
  },
  {
    key: 'practice',
    number: 4,
    icon: TrendingUp,
    name: 'Targeted Practice',
    description: 'Drill your weakest skills with adaptive question sets. Error Bank tracks every miss.',
    link: '/reading/practice',
    ctaLabel: 'Go to practice hub',
  },
  {
    key: 'mastery',
    number: 5,
    icon: Trophy,
    name: 'Mock Tests & Mastery',
    description: 'Full timed mock exams under real conditions. Aim for 3 consecutive passes before your exam date.',
    link: '/mocks?subtest=reading',
    ctaLabel: 'Open mock center',
  },
];

// Derive status from current stage string returned by API
function stageStatus(stageKey: string, currentStage: string): 'complete' | 'current' | 'locked' {
  const order = ['onboarding', 'diagnostic', 'foundation', 'practice', 'mastery'];
  const stageIdx = order.indexOf(stageKey);
  const currentIdx = order.indexOf(currentStage);
  if (stageIdx < currentIdx) return 'complete';
  if (stageIdx === currentIdx) return 'current';
  return 'locked';
}

// ─── Stage card ───────────────────────────────────────────────────────────────

function StageCard({
  config,
  status,
  pathway,
}: {
  config: StageConfig;
  status: 'complete' | 'current' | 'locked';
  pathway: PathwayDto;
}) {
  const Icon = config.icon;
  const isCurrent = status === 'current';
  const isComplete = status === 'complete';
  const isLocked = status === 'locked';

  return (
    <div
      className={cn(
        'relative flex gap-4 rounded-2xl border p-5 transition-shadow',
        isCurrent
          ? 'border-primary/30 bg-primary/5 shadow-md dark:border-violet-700/40 dark:bg-violet-900/10'
          : isComplete
            ? 'border-emerald-200 bg-emerald-50/50 dark:border-emerald-800 dark:bg-emerald-950/20'
            : 'border-border bg-surface opacity-60',
      )}
    >
      {/* Connector line (not on last) */}
      {config.number < 5 && (
        <div className="absolute -bottom-5 left-[2.125rem] top-[4.5rem] w-0.5 bg-border" aria-hidden />
      )}

      {/* Icon */}
      <div
        className={cn(
          'flex h-9 w-9 shrink-0 items-center justify-center rounded-full',
          isCurrent
            ? 'bg-primary text-white'
            : isComplete
              ? 'bg-emerald-500 text-white'
              : 'bg-slate-200 text-slate-500 dark:bg-slate-700 dark:text-slate-400',
        )}
      >
        {isComplete ? <CheckCircle2 className="h-5 w-5" aria-hidden /> : isLocked ? <Lock className="h-4 w-4" aria-hidden /> : <Icon className="h-5 w-5" aria-hidden />}
      </div>

      {/* Content */}
      <div className="flex-1 min-w-0">
        <div className="flex items-start justify-between gap-2 flex-wrap">
          <h3 className="font-semibold text-foreground">{config.name}</h3>
          <Badge
            variant={isCurrent ? 'info' : isComplete ? 'success' : 'default'}
          >
            {isCurrent ? 'Current' : isComplete ? 'Complete' : 'Locked'}
          </Badge>
        </div>

        <p className="mt-1 text-sm text-muted-foreground">{config.description}</p>

        {/* Progress bar for current stage */}
        {isCurrent && pathway.totalWeeks > 0 && (
          <div className="mt-3 space-y-1">
            <div className="flex items-center justify-between text-xs text-muted-foreground">
              <span className="flex items-center gap-1">
                <Clock className="h-3 w-3" aria-hidden />
                Week {pathway.currentWeek} of {pathway.totalWeeks}
              </span>
              <span>{Math.round((pathway.currentWeek / pathway.totalWeeks) * 100)}%</span>
            </div>
            <div className="h-2 w-full rounded-full bg-primary/10 dark:bg-primary/20">
              <div
                className="h-2 rounded-full bg-primary transition-all"
                style={{ width: `${Math.min(100, (pathway.currentWeek / pathway.totalWeeks) * 100)}%` }}
                role="progressbar"
                aria-valuenow={pathway.currentWeek}
                aria-valuemax={pathway.totalWeeks}
              />
            </div>
          </div>
        )}

        {/* CTA */}
        {(isCurrent || isComplete) && (
          <Link
            href={config.link}
            className={cn(
              'mt-3 inline-flex items-center gap-1 text-sm font-medium transition-colors',
              isCurrent
                ? 'text-primary hover:text-primary/80'
                : 'text-emerald-700 hover:text-emerald-600 dark:text-emerald-400',
            )}
          >
            {config.ctaLabel}
            <ChevronRight className="h-4 w-4" aria-hidden />
          </Link>
        )}
      </div>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function ReadingPathwayPage() {
  const [pathway, setPathway] = useState<PathwayDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await getPathway();
        if (!cancelled) setPathway(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load pathway.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <LearnerDashboardShell pageTitle="Reading Pathway">
      <div className="mx-auto max-w-2xl space-y-8">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Your Reading Pathway</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Five stages from onboarding to exam-day mastery. Work through each stage in order.
          </p>
        </div>

        {pathway && (
          <div className="flex flex-wrap gap-6 rounded-2xl border border-border bg-surface p-5 text-sm">
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted-foreground">Readiness</span>
              <span className="text-xl font-bold text-primary">{pathway.readinessScore}%</span>
            </div>
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted-foreground">Weeks remaining</span>
              <span className="text-xl font-bold text-foreground">{pathway.weeksRemaining}</span>
            </div>
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted-foreground">Stage</span>
              <span className="text-xl font-bold text-foreground capitalize">{pathway.currentStage.replace(/_/g, ' ')}</span>
            </div>
          </div>
        )}

        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : loading ? (
          <div className="space-y-4">
            {[1, 2, 3, 4, 5].map((i) => (
              <Skeleton key={i} className="h-28 w-full rounded-2xl" />
            ))}
          </div>
        ) : pathway ? (
          <div className="space-y-5">
            {STAGES.map((stage) => (
              <StageCard
                key={stage.key}
                config={stage}
                status={stageStatus(stage.key, pathway.currentStage)}
                pathway={pathway}
              />
            ))}
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
