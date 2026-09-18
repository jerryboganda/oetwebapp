'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';
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

  if (error) {
    return <p role="alert" className="text-sm text-danger">{error}</p>;
  }

  if (!rows) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Loading your placement history…
      </div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-6 text-sm text-muted">
        No placement attempts yet. Start the free placement test and your results will be saved
        here.
      </div>
    );
  }

  return (
    <ul className="space-y-3">
      {rows.map((row) => (
        <li key={row.id} className="rounded-2xl border border-border bg-surface p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
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
            <Link
              href={`/placement-test/results/${encodeURIComponent(row.id)}`}
              className="rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white transition hover:opacity-90"
            >
              View result
            </Link>
          </div>
        </li>
      ))}
    </ul>
  );
}
