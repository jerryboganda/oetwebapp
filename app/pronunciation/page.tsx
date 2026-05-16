'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Mic, Activity, CalendarClock, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Pronunciation practice"
        description="Practise clinical phonemes, minimal-pair listening, and speaking-linked pronunciation drills."
        icon={Mic}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {entitlement && !entitlement.allowed && (
        <InlineAlert variant="info" title="Practice limit reached" className="mb-4">
          Your {entitlement.tier} plan refreshes pronunciation attempts
          {entitlement.resetAt ? ` on ${new Date(entitlement.resetAt).toLocaleDateString()}` : ' at the next billing window'}.
        </InlineAlert>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <Card className="p-4">
          <Activity className="mb-3 h-5 w-5 text-primary" />
          <p className="text-sm font-semibold text-navy">Rulebook-grounded scoring</p>
          <p className="mt-1 text-xs leading-5 text-muted">Attempts are scored by the server-authoritative pronunciation pipeline.</p>
        </Card>
        <Card className="p-4">
          <CalendarClock className="mb-3 h-5 w-5 text-primary" />
          <p className="text-sm font-semibold text-navy">Due drills first</p>
          <p className="mt-1 text-xs leading-5 text-muted">Your queue prioritises phonemes that need spaced-repetition practice.</p>
        </Card>
        <Card className="p-4">
          <Lock className="mb-3 h-5 w-5 text-primary" />
          <p className="text-sm font-semibold text-navy">Protected audio</p>
          <p className="mt-1 text-xs leading-5 text-muted">Audio uploads stay behind authenticated learner endpoints.</p>
        </Card>
      </div>

      <section className="mt-6">
        <LearnerSurfaceSectionHeader title="Due pronunciation drills" />
        {loading ? (
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            {[1, 2, 3, 4].map((item) => <Skeleton key={item} className="h-32 rounded-xl" />)}
          </div>
        ) : drills.length === 0 ? (
          <Card className="mt-3 p-6 text-sm text-muted">
            No pronunciation drills are due. Check back after your next speaking practice.
          </Card>
        ) : (
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            {drills.map((drill) => (
              <Card key={drill.id} className="flex flex-col gap-4 p-4">
                <div>
                  <div className="mb-2 flex flex-wrap items-center gap-2">
                    <Badge variant="outline">{drill.difficulty}</Badge>
                    <Badge variant="muted">{drill.focus}</Badge>
                  </div>
                  <p className="text-sm font-semibold text-navy">{drill.label}</p>
                  <p className="mt-1 text-xs text-muted">{drill.targetPhoneme} · {drill.profession}</p>
                </div>
                <Link href={`/pronunciation/${encodeURIComponent(drill.id)}`} className="self-start">
                  <Button size="sm">Open drill</Button>
                </Link>
              </Card>
            ))}
          </div>
        )}
      </section>
    </LearnerDashboardShell>
  );
}
