'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { RefreshCcw } from 'lucide-react';
import { TutorRouteSectionHeader } from '@/components/domain/tutor-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { apiClient } from '@/lib/api';

interface WritingReviewQueueItem {
  submissionId: string;
  userId: string;
  profession: string;
  letterType: string;
  wordCount: number;
  requestedAt: string;
  claimedAt: string | null;
  claimedByTutorId: string | null;
  status: string;
}

const STATUSES: Array<{ id: string; label: string }> = [
  { id: '', label: 'All' },
  { id: 'pending', label: 'Pending' },
  { id: 'claimed', label: 'Claimed' },
  { id: 'in-review', label: 'In review' },
  { id: 'submitted', label: 'Submitted' },
];

/**
 * Shared Writing V2 review queue (claim a submission, then open the submission-keyed
 * marking workspace). Backed by `/v1/tutors/writing/queue`, which is the single home
 * for writing reviews — the expert IS the tutor for writing. Rendered inside both the
 * expert console (`/expert/queue/assigned`, `reviewHrefBase="/expert/review/writing"`)
 * and the tutor console (`/tutor/writing/queue`, `reviewHrefBase="/tutor/writing/reviews"`);
 * the only difference is which submission-keyed marking route an opened review lands on.
 */
export function WritingReviewQueue({
  reviewHrefBase,
  initialStatus = 'pending',
}: {
  reviewHrefBase: string;
  initialStatus?: string;
}) {
  const [items, setItems] = useState<WritingReviewQueueItem[]>([]);
  const [status, setStatus] = useState<string>(initialStatus);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const base = reviewHrefBase.replace(/\/$/, '');

  const load = useCallback(async () => {
    try {
      const r = await apiClient.get<{ items: WritingReviewQueueItem[] }>(
        `/v1/tutors/writing/queue${status ? `?status=${encodeURIComponent(status)}` : ''}`,
      );
      setItems(r.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load queue.');
    }
  }, [status]);

  useEffect(() => {
    void load();
  }, [load]);

  const claim = async (submissionId: string) => {
    setBusy(`claim-${submissionId}`);
    try {
      await apiClient.post(`/v1/tutors/writing/queue/${encodeURIComponent(submissionId)}/claim`, {});
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Claim failed.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-border bg-surface p-3 shadow-sm">
        <fieldset className="flex flex-wrap items-center gap-2" aria-label="Filter queue">
          <legend className="sr-only">Filter queue</legend>
          <span className="text-xs font-bold uppercase tracking-wider text-muted">Status:</span>
          {STATUSES.map((s) => (
            <button
              key={s.id || 'all'}
              type="button"
              onClick={() => setStatus(s.id)}
              aria-pressed={status === s.id}
              className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${status === s.id ? 'border-primary bg-primary text-white dark:bg-violet-700' : 'border-border bg-background text-navy hover:border-primary/40 dark:text-slate-200'}`}
            >
              {s.label}
            </button>
          ))}
        </fieldset>
        <Button variant="outline" size="sm" onClick={() => void load()}><RefreshCcw className="h-3 w-3" aria-hidden="true" /> Refresh</Button>
      </div>

      <TutorRouteSectionHeader eyebrow="Queue" title={`${items.length} submission${items.length === 1 ? '' : 's'}`} description="Claim a submission to lock it to you, then open it to mark." className="mt-4 mb-3" />

      <Card>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm" aria-label="Writing tutor review queue">
              <thead>
                <tr className="border-b border-border bg-background-light/50 text-xs font-semibold uppercase tracking-wider text-muted">
                  <th className="px-4 py-3 text-left">Submission</th>
                  <th className="px-4 py-3 text-left">Profession</th>
                  <th className="px-4 py-3 text-left">Letter</th>
                  <th className="px-4 py-3 text-right">Words</th>
                  <th className="px-4 py-3 text-left">Requested</th>
                  <th className="px-4 py-3 text-left">Status</th>
                  <th className="px-4 py-3 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {items.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-sm text-muted">
                      Queue is empty.
                    </td>
                  </tr>
                ) : null}
                {items.map((row) => (
                  <tr key={row.submissionId} className="transition-colors hover:bg-background-light/40">
                    <td className="px-4 py-3 font-mono text-xs font-bold text-navy dark:text-foreground">
                      {row.submissionId.slice(0, 8)}…
                    </td>
                    <td className="px-4 py-3 capitalize text-muted">{row.profession}</td>
                    <td className="px-4 py-3 text-navy dark:text-foreground">{row.letterType}</td>
                    <td className="px-4 py-3 text-right text-muted">{row.wordCount}</td>
                    <td className="px-4 py-3 text-xs text-muted">{new Date(row.requestedAt).toLocaleString()}</td>
                    <td className="px-4 py-3">
                      <Badge variant={row.status === 'pending' ? 'warning' : row.status === 'submitted' ? 'success' : 'info'} size="sm">
                        {row.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {row.status === 'pending' ? (
                        <Button size="sm" onClick={() => void claim(row.submissionId)} loading={busy === `claim-${row.submissionId}`}>
                          Claim
                        </Button>
                      ) : (
                        <Button asChild size="sm" variant="outline">
                          <Link href={`${base}/${encodeURIComponent(row.submissionId)}`}>Open review</Link>
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </>
  );
}
