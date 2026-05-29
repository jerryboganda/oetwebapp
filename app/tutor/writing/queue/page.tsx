'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ClipboardList, RefreshCcw } from 'lucide-react';
import { TutorRouteHero, TutorRouteSectionHeader, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { apiClient } from '@/lib/api';

interface TutorQueueItem {
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

export default function TutorWritingQueuePage() {
  const [items, setItems] = useState<TutorQueueItem[]>([]);
  const [status, setStatus] = useState<string>('pending');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const r = await apiClient.get<{ items: TutorQueueItem[] }>(`/v1/tutors/writing/queue${status ? `?status=${encodeURIComponent(status)}` : ''}`);
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
    <TutorRouteWorkspace>
      <TutorRouteHero
        eyebrow="Tutor portal"
        icon={ClipboardList}
        title="Writing review queue"
        description="Claim a submission to start a review. Each claim auto-expires after 36 hours."
      />

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
              className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${status === s.id ? 'border-primary bg-primary text-white dark:bg-violet-700' : 'border-border bg-background text-navy hover:border-primary/40'}`}
            >
              {s.label}
            </button>
          ))}
        </fieldset>
        <Button variant="outline" size="sm" onClick={() => void load()}><RefreshCcw className="h-3 w-3" aria-hidden="true" /> Refresh</Button>
      </div>

      <TutorRouteSectionHeader eyebrow="Queue" title={`${items.length} submission${items.length === 1 ? '' : 's'}`} description="Click claim to lock the submission to you for review." className="mt-4 mb-3" />

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Writing tutor review queue">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Submission</th>
                <th className="text-left">Profession</th>
                <th className="text-left">Letter</th>
                <th className="text-right">Words</th>
                <th className="text-left">Requested</th>
                <th className="text-left">Status</th>
                <th className="text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? <tr><td colSpan={7} className="py-4 text-center text-xs text-muted">Queue is empty.</td></tr> : null}
              {items.map((row) => (
                <tr key={row.submissionId} className="border-b border-border/60">
                  <td className="py-2 font-bold text-xs">{row.submissionId.slice(0, 8)}…</td>
                  <td className="capitalize">{row.profession}</td>
                  <td>{row.letterType}</td>
                  <td className="text-right">{row.wordCount}</td>
                  <td className="text-xs text-muted">{new Date(row.requestedAt).toLocaleString()}</td>
                  <td><Badge variant={row.status === 'pending' ? 'warning' : row.status === 'submitted' ? 'success' : 'info'} size="sm">{row.status}</Badge></td>
                  <td className="text-right">
                    {row.status === 'pending' ? (
                      <Button size="sm" onClick={() => void claim(row.submissionId)} loading={busy === `claim-${row.submissionId}`}>Claim</Button>
                    ) : (
                      <Button asChild size="sm" variant="outline">
                        <Link href={`/tutor/writing/reviews/${encodeURIComponent(row.submissionId)}`}>Open review</Link>
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>
    </TutorRouteWorkspace>
  );
}
