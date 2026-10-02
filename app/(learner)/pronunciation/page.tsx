'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Mic } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  fetchPronunciationDueDrills,
  fetchPronunciationEntitlement,
  type PronunciationDrillSummary,
  type PronunciationEntitlement,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

export default function PronunciationPage() {
  const [drills, setDrills] = useState<PronunciationDrillSummary[]>([]);
  const [entitlement, setEntitlement] = useState<PronunciationEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    analytics.track('pronunciation_page_viewed');

    Promise.all([
      fetchPronunciationDueDrills(6) as Promise<PronunciationDrillSummary[]>,
      fetchPronunciationEntitlement() as Promise<PronunciationEntitlement>,
    ])
      .then(([dueDrills, entitlementState]) => {
        if (cancelled) return;
        setDrills(dueDrills);
        setEntitlement(entitlementState);
      })
      .catch(() => {
        if (!cancelled) {
          setError('Pronunciation drills are not available right now.');
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <>
      <LearnerPageHero
        title="Pronunciation practice"
        description="Practise clinical phonemes, minimal-pair listening, and speaking-linked pronunciation drills."
        icon={Mic}
      />

      {entitlement && !entitlement.allowed && (
        <InlineAlert variant="info" title="Practice limit reached">
          Your {entitlement.tier} plan refreshes pronunciation attempts
          {entitlement.resetAt ? ` on ${new Date(entitlement.resetAt).toLocaleDateString()}` : ' at the next billing window'}.
        </InlineAlert>
      )}

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader title="Due pronunciation drills" />
        {loading ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {[1, 2, 3, 4].map((item) => <Skeleton key={item} className="h-32 rounded-2xl" />)}
          </div>
        ) : error ? (
          <InlineAlert variant="warning">{error}</InlineAlert>
        ) : drills.length === 0 ? (
          <EmptyState
            icon={<Mic className="h-7 w-7" aria-hidden="true" />}
            title="No pronunciation drills are due"
            description="Check back after your next speaking practice."
          />
        ) : (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {drills.map((drill, i) => (
              <MotionItem key={drill.id} delayIndex={Math.min(i, 5)} className="h-full">
                <Card className="flex h-full flex-col gap-4">
                  <div>
                    <div className="mb-2 flex flex-wrap items-center gap-2">
                      <Badge variant="outline">{drill.difficulty}</Badge>
                      <Badge variant="muted">{drill.focus}</Badge>
                    </div>
                    <h3 className="text-sm font-bold text-navy">{drill.label}</h3>
                    <p className="mt-1 text-xs text-muted">{drill.targetPhoneme} · {drill.profession}</p>
                  </div>
                  <Button size="sm" asChild className="mt-auto self-start">
                    <Link href={`/pronunciation/${encodeURIComponent(drill.id)}`}>Open drill</Link>
                  </Button>
                </Card>
              </MotionItem>
            ))}
          </div>
        )}
      </MotionSection>
    </>
  );
}
