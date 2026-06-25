'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowRight, CheckCircle2, Circle, Sparkles } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, ProgressBar } from '@/components/ui';
import { fetchUserProfile } from '@/lib/api';
import type { UserProfile } from '@/lib/mock-data';
import { trackChecklistItemCompleted } from '@/lib/onboarding/tour-events';
import { useTourSafe } from './tour-provider';

interface ChecklistItem {
  id: string;
  label: string;
  done: boolean;
  cta: string;
  onClick: () => void;
}

/**
 * Contextual "Get started" checklist for the learner dashboard. Completion is
 * derived from real signals (profile fields, dashboard-tour flag, diagnostic
 * evidence) — never fabricated — and the card hides itself once the core setup
 * is finished. Uses useState+useEffect (not React Query) so it works in the
 * same test environment as the rest of the dashboard (vi.mock('@/lib/api')).
 */
export function OnboardingChecklist() {
  const router = useRouter();
  const tour = useTourSafe();
  const isAuthenticated = tour?.isAuthenticated ?? false;
  const isCompleted = tour?.isCompleted ?? (() => false);
  const startTour = tour?.startTour ?? (() => Promise.resolve());

  const [profile, setProfile] = useState<UserProfile | null>(null);

  useEffect(() => {
    if (!isAuthenticated) return;
    let cancelled = false;
    fetchUserProfile()
      .then((p) => { if (!cancelled) setProfile(p); })
      .catch(() => { /* silently degrade — checklist hides until loaded */ });
    return () => { cancelled = true; };
  }, [isAuthenticated]);

  const items = useMemo<ChecklistItem[]>(() => {
    const hasTarget = profile
      ? Object.values(profile.targetScores).some((value) => value != null)
      : false;
    return [
      {
        id: 'profession',
        label: 'Choose your profession',
        done: Boolean(profile?.profession),
        cta: 'Set profession',
        onClick: () => router.push('/goals'),
      },
      {
        id: 'exam-date',
        label: 'Set your target exam date',
        done: Boolean(profile?.examDate),
        cta: 'Add date',
        onClick: () => router.push('/goals'),
      },
      {
        id: 'target-scores',
        label: 'Set a target score',
        done: hasTarget,
        cta: 'Set targets',
        onClick: () => router.push('/goals'),
      },
      {
        id: 'platform-tour',
        label: 'Take the platform tour',
        done: isCompleted('dashboard'),
        cta: 'Start tour',
        onClick: () => void startTour('learner-dashboard', { replay: true }),
      },
    ];
  }, [profile, isCompleted, router, startTour]);

  // Fire `checklist_item_completed` only on a real not-done→done transition.
  const prevDone = useRef<Set<string> | null>(null);
  useEffect(() => {
    const done = new Set(items.filter((item) => item.done).map((item) => item.id));
    if (prevDone.current === null) {
      prevDone.current = done;
      return;
    }
    for (const id of done) {
      if (!prevDone.current.has(id)) trackChecklistItemCompleted({ itemId: id });
    }
    prevDone.current = done;
  }, [items]);

  const completedCount = items.filter((item) => item.done).length;

  // Hide before profile loads, or once all items are done.
  if (!profile || completedCount === items.length) return null;

  const percent = Math.round((completedCount / items.length) * 100);

  return (
    <Card data-tour="learner-dashboard-checklist">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <CardTitle>
            <Sparkles className="mr-1.5 inline-block h-4.5 w-4.5 text-primary" aria-hidden="true" />
            Get started
          </CardTitle>
          <span className="text-xs font-semibold text-muted">
            {completedCount} of {items.length} done
          </span>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <ProgressBar value={percent} ariaLabel={`Setup ${percent}% complete`} color="primary" />
        <ul className="space-y-1.5">
          {items.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                onClick={item.onClick}
                disabled={item.done}
                className="group flex w-full items-center justify-between gap-3 rounded-xl px-2 py-2 text-left transition-colors enabled:hover:bg-primary/5 disabled:cursor-default focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <span className="flex items-center gap-2.5">
                  {item.done ? (
                    <CheckCircle2 className="h-5 w-5 shrink-0 text-success" aria-hidden="true" />
                  ) : (
                    <Circle className="h-5 w-5 shrink-0 text-muted/50" aria-hidden="true" />
                  )}
                  <span className={`text-sm ${item.done ? 'text-muted line-through' : 'font-semibold text-navy'}`}>
                    {item.label}
                  </span>
                </span>
                {!item.done ? (
                  <span className="flex items-center gap-1 text-xs font-bold text-primary opacity-0 transition-opacity group-hover:opacity-100">
                    {item.cta}
                    <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </span>
                ) : null}
              </button>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}
