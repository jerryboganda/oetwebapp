'use client';

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - learner-facing list of
// published speaking mock sets (two role-plays attempted as one mock)
// + free-tier rolling 7-day usage indicator.

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { fetchSpeakingMockSets, type SpeakingMockSetSummary, type SpeakingMockSetEntitlement } from '@/lib/api';
import { analytics } from '@/lib/analytics';

export default function SpeakingMockSetIndexPage() {
  const [items, setItems] = useState<SpeakingMockSetSummary[]>([]);
  const [entitlement, setEntitlement] = useState<SpeakingMockSetEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    fetchSpeakingMockSets()
      .then(({ mockSets, entitlement: ent }) => {
        if (!active) return;
        setItems(mockSets);
        setEntitlement(ent);
      })
      .catch((e: unknown) => {
        if (!active) return;
        setError(e instanceof Error ? e.message : 'Could not load mock sets.');
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const remaining = entitlement?.remaining ?? 0;
  const blocked = remaining === 0 && (entitlement?.cap ?? 0) > 0;

  return (
    <LearnerDashboardShell pageTitle="Speaking mock sets">
      <div className="mx-auto w-full max-w-5xl space-y-6 p-4 sm:p-6">
        <header className="space-y-2">
          <h1 className="text-3xl font-black">Speaking mock sets</h1>
          <p className="text-sm text-muted">
            Each mock set pairs two role-plays — exactly like the official OET Speaking sub-test. Take both back-to-back for a realistic combined readiness band.
          </p>
        </header>

        {entitlement && (
          <div
            role="status"
            aria-live="polite"
            className={`rounded-2xl border p-4 text-sm ${
              blocked
                ? 'border-danger/30 bg-danger/5 text-danger'
                : remaining <= 1
                  ? 'border-warning/30 bg-amber-50 text-warning'
                  : 'border-border bg-surface text-muted'
            }`}
          >
            <strong className="font-bold">{remaining}</strong> of <strong>{entitlement.cap}</strong> mock {entitlement.cap === 1 ? 'set' : 'sets'} remaining this week
            {blocked ? '. Upgrade to continue, or wait until next week.' : '.'}
          </div>
        )}

        {loading && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Skeleton className="h-44" />
            <Skeleton className="h-44" />
          </div>
        )}

        {!loading && error && (
          <EmptyState
            title="Couldn't load mock sets"
            description={error}
          />
        )}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            title="No mock sets are published yet"
            description="Mock sets pair two speaking role-plays. They'll appear here once an admin publishes one."
          />
        )}

        {!loading && !error && items.length > 0 && (
          <ul className="grid gap-4 sm:grid-cols-2">
            {items.map((set) => (
              <li
                key={set.mockSetId}
                className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-5 shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <h2 className="text-lg font-black">{set.title}</h2>
                  <Badge variant="info" className="capitalize">{set.difficulty}</Badge>
                </div>
                {set.description && (
                  <p className="text-sm text-muted">{set.description}</p>
                )}
                {set.criteriaFocus.length > 0 && (
                  <div className="flex flex-wrap gap-1.5">
                    {set.criteriaFocus.map((c) => (
                      <Badge key={c} variant="muted" className="text-[10px] uppercase tracking-wider">
                        {c}
                      </Badge>
                    ))}
                  </div>
                )}
                <div className="mt-auto flex items-center justify-between gap-3 pt-2">
                  <p className="text-xs text-muted">
                    Two role-plays · combined readiness band
                  </p>
                  <Link
                    href={`/speaking/mocks/${set.mockSetId}`}
                    onClick={() => analytics.track('speaking_mock_set_card_opened', { mockSetId: set.mockSetId })}
                  >
                    <Button variant={blocked ? 'outline' : 'primary'} disabled={blocked}>
                      {blocked ? 'Cap reached' : 'Start mock'}
                    </Button>
                  </Link>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
