'use client';

import { useEffect, useState } from 'react';
import { ClipboardCheck, Clock3, Headphones, Mic } from 'lucide-react';
import { LearnerSurfaceCard } from '@/components/domain';
import { loadPlacementAccess } from '@/hooks/use-placement-access';
import {
  PLACEMENT_ACTIVE_SESSION_KEY,
  fetchPlacementHistory,
  fetchPlacementSessionState,
  type PlacementHistoryItem,
} from '@/lib/api/placement';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

type CardState =
  | { kind: 'start' }
  | { kind: 'continue'; latest: PlacementHistoryItem | null }
  | { kind: 'results'; latest: PlacementHistoryItem };

async function hasOpenAttempt(): Promise<boolean> {
  let sessionId: string | null = null;
  try {
    sessionId = window.localStorage.getItem(PLACEMENT_ACTIVE_SESSION_KEY);
  } catch {
    return false;
  }
  if (!sessionId) return false;
  try {
    return !(await fetchPlacementSessionState(sessionId)).has_result;
  } catch {
    return false;
  }
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

function toModel(state: CardState): LearnerSurfaceCardModel {
  const base = {
    kind: 'navigation',
    sourceType: 'frontend_status',
    accent: 'indigo',
    eyebrow: 'Free English Placement Test',
    eyebrowIcon: ClipboardCheck,
  } as const;

  if (state.kind === 'continue') {
    return {
      ...base,
      statusLabel: 'In progress',
      title: 'Continue your placement test',
      description: 'Your answers are saved. Pick up where you left off.',
      metaItems: [{ icon: Clock3, label: 'Saves as you go' }],
      primaryAction: { label: 'Continue', href: '/placement-test' },
      secondaryAction: state.latest
        ? { label: 'View Placement Test Results', href: `/placement-test/results/${encodeURIComponent(state.latest.id)}`, variant: 'secondary' }
        : undefined,
    };
  }

  if (state.kind === 'results') {
    const date = formatDate(state.latest.createdAt);
    return {
      ...base,
      statusLabel: state.latest.status === 'completed' ? 'Completed' : 'Partial result',
      title: 'Your placement test results',
      description: `Your indicative CEFR placement estimate${date ? ` from ${date}` : ''} is saved to your profile. Retake it any time to track progress.`,
      metaItems: [{ icon: Clock3, label: date ? `Last attempt ${date}` : 'Saved to your profile' }],
      primaryAction: {
        label: 'View Placement Test Results',
        href: `/placement-test/results/${encodeURIComponent(state.latest.id)}`,
      },
      secondaryAction: { label: 'Retake', href: '/placement-test', variant: 'secondary' },
    };
  }

  return {
    ...base,
    statusLabel: 'Not started',
    title: 'Find your English level — free',
    description:
      'Listening, Reading, Writing and Speaking. CEFR Pre-A1 to C2. Suitable for OET, IELTS, PTE, TOEFL and General English.',
    metaItems: [
      { icon: Clock3, label: 'About 60–85 minutes' },
      { icon: Headphones, label: 'Headphones' },
      { icon: Mic, label: 'Microphone' },
    ],
    primaryAction: { label: 'Start Placement Test', href: '/placement-test' },
  };
}

/**
 * Dashboard entry for the free placement test: Start / Continue / View
 * Results. Renders nothing unless the learner can open the test, and loads
 * independently so it can never delay or break the rest of the dashboard.
 */
export function PlacementDashboardCard({ className }: { className?: string }) {
  const [state, setState] = useState<CardState | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!(await loadPlacementAccess())) return;
      const [history, open] = await Promise.all([
        fetchPlacementHistory().catch(() => [] as PlacementHistoryItem[]),
        hasOpenAttempt(),
      ]);
      if (cancelled) return;
      const latest = history[0] ?? null;
      if (open) setState({ kind: 'continue', latest });
      else if (latest) setState({ kind: 'results', latest });
      else setState({ kind: 'start' });
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!state) return null;
  return <LearnerSurfaceCard card={toModel(state)} className={className} />;
}
