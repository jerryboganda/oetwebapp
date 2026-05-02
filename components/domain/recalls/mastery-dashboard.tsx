'use client';

import { useEffect, useState } from 'react';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { fetchRecallsLibrary, type RecallsLibraryItem } from '@/lib/api';

const BUCKETS = [
  { key: 'all', label: 'All' },
  { key: 'starred', label: 'Starred' },
  { key: 'weak', label: 'Weak' },
  { key: 'new', label: 'New' },
  { key: 'mastered', label: 'Mastered' },
] as const;

type BucketKey = (typeof BUCKETS)[number]['key'];

const MASTERY_BADGES: Record<string, 'success' | 'info' | 'warning' | 'muted'> = {
  mastered: 'success',
  reviewing: 'info',
  learning: 'warning',
  new: 'muted',
};

export function MasteryDashboard() {
  const [bucket, setBucket] = useState<BucketKey>('all');
  const [items, setItems] = useState<RecallsLibraryItem[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchRecallsLibrary({ bucket: bucket === 'all' ? undefined : bucket })
      .then((r) => {
        if (cancelled) return;
        setItems(r.items);
        setError(null);
      })
      .catch(() => {
        if (!cancelled) setError('Could not load your library.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [bucket]);

  const totals = computeTotals(items ?? []);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {[
          { label: 'Total', value: totals.total, color: 'text-navy' },
          { label: 'Mastered', value: totals.mastered, color: 'text-success' },
          { label: 'Weak', value: totals.weak, color: 'text-warning' },
          { label: 'Starred', value: totals.starred, color: 'text-info' },
        ].map((s) => (
          <Card key={s.label} className="rounded-2xl p-4 text-center shadow-sm">
            <div className={`text-3xl font-bold ${s.color}`}>{s.value}</div>
            <div className="mt-1 text-xs text-muted">{s.label}</div>
          </Card>
        ))}
      </div>

      <div className="flex flex-wrap gap-2">
        {BUCKETS.map((b) => (
          <button
            key={b.key}
            type="button"
            onClick={() => setBucket(b.key)}
            aria-pressed={bucket === b.key}
            className={`rounded-full border px-3 py-1 text-sm font-medium transition-colors ${
              bucket === b.key
                ? 'border-primary bg-lavender/40 text-navy'
                : 'border-border bg-surface text-muted hover:border-border-hover'
            }`}
          >
            {b.label}
          </button>
        ))}
      </div>

      {error && <div className="rounded-xl border border-warning/30 bg-warning/10 p-3 text-sm text-warning">{error}</div>}

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-14 rounded-xl" />
          ))}
        </div>
      ) : items && items.length > 0 ? (
        <ul className="divide-y divide-border rounded-2xl border border-border bg-surface">
          {items.map((it) => (
            <li key={it.cardId} className="flex items-center gap-3 p-3">
              <div className="flex-1">
                <div className="flex items-center gap-2">
                  <span className="font-semibold text-navy">{it.term}</span>
                  {it.starred && <Badge variant="warning">Starred</Badge>}
                  <Badge variant={MASTERY_BADGES[it.mastery] ?? 'muted'}>{it.mastery}</Badge>
                  {it.lastErrorTypeCode && it.lastErrorTypeCode !== 'correct' && (
                    <Badge variant="danger">{it.lastErrorTypeCode}</Badge>
                  )}
                </div>
                <div className="text-xs text-muted">
                  {it.category} · reviewed {it.reviewCount} · correct {it.correctCount} · interval {it.intervalDays}d
                </div>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <Card className="rounded-2xl p-6 text-center text-sm text-muted">No cards in this bucket yet.</Card>
      )}
    </div>
  );
}

function computeTotals(items: RecallsLibraryItem[]) {
  return {
    total: items.length,
    mastered: items.filter((i) => i.mastery === 'mastered').length,
    weak: items.filter((i) => i.reviewCount > 0 && i.correctCount / i.reviewCount < 0.7).length,
    starred: items.filter((i) => i.starred).length,
  };
}
