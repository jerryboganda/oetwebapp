'use client';

import { useEffect, useState } from 'react';
import { Heart } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge, RecallTierBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
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
    <>
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

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Review later"
          title="Favourited words"
          description="Tap the heart on any word in the catalog to add it here."
        />

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-14 rounded-xl" />
            ))}
          </div>
        ) : error ? (
          <InlineAlert variant="warning">{error}</InlineAlert>
        ) : items && items.length > 0 ? (
          <Card padding="none">
            <ul className="divide-y divide-border">
              {items.map((it, i) => (
                <li key={it.cardId}>
                  <MotionItem delayIndex={Math.min(i, 5)} className="flex items-center gap-3 p-3 sm:px-4">
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-semibold text-navy">{it.term}</span>
                        <RecallTierBadge
                          count={it.examFrequencyCount ?? 0}
                          occurrences={it.recallSetOccurrences}
                          lastUpdatedAt={it.updatedAt}
                        />
                        <Badge variant={MASTERY_BADGES[it.mastery] ?? 'muted'}>{it.mastery}</Badge>
                        {it.starReason && <Badge variant="warning">{it.starReason}</Badge>}
                      </div>
                      {it.definition && <div className="mt-1 text-xs text-muted">{it.definition}</div>}
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleRemove(it)}
                      aria-label={`Remove ${it.term} from favourites`}
                      className="shrink-0"
                    >
                      <Heart className="h-3.5 w-3.5 fill-current text-warning-strong" aria-hidden="true" />
                      Remove
                    </Button>
                  </MotionItem>
                </li>
              ))}
            </ul>
          </Card>
        ) : (
          <EmptyState
            icon={<Heart className="h-7 w-7 text-warning-strong" aria-hidden="true" />}
            title="No favourites yet"
            description="Browse the vocabulary catalog and tap the heart on any word to save it for later."
            action={{ label: 'Browse words', href: '/recalls/words' }}
          />
        )}
      </MotionSection>
    </>
  );
}
