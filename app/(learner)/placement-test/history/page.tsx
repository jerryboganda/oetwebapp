'use client';

import { useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { ClipboardList, History } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchPlacementHistory, type PlacementHistoryItem } from '@/lib/api/placement';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * Past placement attempts for the signed-in learner. Rows are the OET-owned
 * `PlacementResults` history — they survive engine-side retention and link
 * to the standalone result view.
 */
export default function PlacementHistoryPage() {
  const [rows, setRows] = useState<PlacementHistoryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchPlacementHistory()
      .then((history) => {
        if (!cancelled) setRows(history);
      })
      .catch((err) => {
        if (!cancelled) setError(readErrorMessage(err, 'Could not load your placement history.'));
      });
    return () => {
      cancelled = true;
    };
  }, []);

  let body: ReactNode;
  if (error) {
    body = <ErrorState title="Placement history unavailable" message={error} />;
  } else if (!rows) {
    body = (
      <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading your placement history">
        {[0, 1, 2].map((i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}
      </div>
    );
  } else if (rows.length === 0) {
    body = (
      <EmptyState
        icon={<ClipboardList className="h-7 w-7" aria-hidden="true" />}
        title="No placement attempts yet"
        description="Start the free placement test and your results will be saved here."
        action={{ label: 'Start placement test', href: '/placement-test' }}
      />
    );
  } else {
    body = (
      <ul className="space-y-3">
        {rows.map((row, i) => (
          <li key={row.id}>
            <MotionItem delayIndex={Math.min(i, 5)}>
              <Card className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-navy">
                    Attempt of {new Date(row.createdAt).toLocaleDateString(undefined, {
                      year: 'numeric',
                      month: 'long',
                      day: 'numeric',
                    })}
                  </p>
                  <p className="mt-0.5 text-xs text-muted">
                    {row.status === 'completed' ? 'All four skills measured' : 'Partial profile'}
                    {' · '}ruleset {row.rulesetVersion}
                  </p>
                </div>
                <Button asChild size="sm">
                  <Link href={`/placement-test/results/${encodeURIComponent(row.id)}`}>View result</Link>
                </Button>
              </Card>
            </MotionItem>
          </li>
        ))}
      </ul>
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow="Placement test"
        title="Your placement history"
        description="Every placement attempt you have taken. Open any attempt to see its full skill profile."
        icon={History}
      />
      {body}
    </>
  );
}
