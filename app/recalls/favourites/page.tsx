'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Heart } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  fetchRecallsLibrary,
  fetchRecallsToday,
  starRecall,
  type RecallsLibraryItem,
  type RecallsTodayResponse,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { toast } from 'sonner';

const MASTERY_BADGES: Record<string, 'success' | 'info' | 'warning' | 'muted'> = {
  mastered: 'success',
  reviewing: 'info',
  learning: 'warning',
  new: 'muted',
};

/**
 * /recalls/favourites — the learner's "review later" list. Reuses the existing
 * `starred` library bucket (favourites == starred cards) so there is a single
 * source of truth. From here learners can view or remove a favourite.
 */
export default function RecallsFavouritesPage() {
  const [today, setToday] = useState<RecallsTodayResponse | null>(null);
  const [items, setItems] = useState<RecallsLibraryItem[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('recalls_favourites_viewed');
    let cancelled = false;
    fetchRecallsToday().then((t) => { if (!cancelled) setToday(t); }).catch(() => undefined);
    fetchRecallsLibrary({ bucket: 'starred' })
      .then((r) => { if (!cancelled) { setItems(r.items); setError(null); } })
      .catch(() => { if (!cancelled) setError('Could not load your favourites.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  async function handleRemove(item: RecallsLibraryItem) {
    const previous = items;
    setItems((prev) => (prev ? prev.filter((p) => p.cardId !== item.cardId) : prev));
    try {
      await starRecall('vocab', item.cardId, false);
    } catch {
      setItems(previous ?? null);
      toast.error('Could not remove favourite');
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Favourites"
          title="Your saved words to review later"
          description="Every word you favourited, in one place. Remove what you've mastered."
          icon={Heart}
          highlights={[
            // Prefer the live list length so the count stays in sync after a
            // removal; fall back to today's snapshot only while items load.
            { icon: Heart, label: 'Favourites', value: `${items?.length ?? today?.starred ?? 0}` },
          ]}
        />

        <LearnerSurfaceSectionHeader
          eyebrow="Review later"
          title="Favourited words"
          description="Tap the heart on any word in the catalog to add it here."
        />

        {error && (
          <div className="rounded-xl border border-warning/30 bg-warning/10 p-3 text-sm text-warning">{error}</div>
        )}

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
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-navy">{it.term}</span>
                      <Badge variant={MASTERY_BADGES[it.mastery] ?? 'muted'}>{it.mastery}</Badge>
                      {it.starReason && <Badge variant="warning">{it.starReason}</Badge>}
                    </div>
                    {it.definition && <div className="mt-1 text-xs text-muted">{it.definition}</div>}
                  </div>
                  <button
                    type="button"
                    onClick={() => handleRemove(it)}
                    aria-label={`Remove ${it.term} from favourites`}
                    className="inline-flex items-center gap-1 rounded-full border border-warning/40 bg-warning/10 px-3 py-1 text-xs font-medium text-warning hover:border-warning"
                  >
                    <Heart size={13} className="fill-current" aria-hidden="true" />
                    Remove
                  </button>
                </li>
              ))}
            </ul>
        ) : (
          <div className="rounded-2xl border border-dashed border-border p-8 text-center">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-warning/10 text-warning">
              <Heart className="h-6 w-6" />
            </div>
            <p className="mt-3 text-sm font-semibold text-navy">No favourites yet</p>
            <p className="mt-1 text-sm text-muted">
              Browse the vocabulary catalog and tap the heart on any word to save it for later.
            </p>
            <div className="mt-4">
              <Button asChild variant="secondary">
                <Link href="/recalls/words">Browse words</Link>
              </Button>
            </div>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
