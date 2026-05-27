'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { DrillPlayer } from '@/components/domain/speaking/DrillPlayer';
import {
  listSpeakingDrills,
  startDrillAttempt,
  type DrillAttemptDetail,
  type DrillScoringResponse,
  type DrillSummary,
} from '@/lib/api/speaking-drills';

export default function SpeakingDrillPlayerPage() {
  const params = useParams<{ id: string }>();
  const rawId = params?.id;
  const drillId = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';
  const startedForRef = useRef<string | null>(null);

  const [drill, setDrill] = useState<DrillSummary | null>(null);
  const [attempt, setAttempt] = useState<DrillAttemptDetail | null>(null);
  const [feedback, setFeedback] = useState<DrillScoringResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!drillId || startedForRef.current === drillId) return;
    startedForRef.current = drillId;
    let cancelled = false;
    setLoading(true);
    setError(null);

    (async () => {
      try {
        const list = await listSpeakingDrills();
        const selected = list.items.find((item) => item.drillId === drillId || item.id === drillId);
        if (!selected) {
          throw new Error('This speaking drill is not available.');
        }
        const createdAttempt = await startDrillAttempt(selected.drillId || drillId, 'ManualBrowse');
        if (cancelled) return;
        setDrill(selected);
        setAttempt(createdAttempt);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not start this speaking drill.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [drillId]);

  return (
    <LearnerDashboardShell pageTitle="Speaking drill">
      <div className="mx-auto max-w-4xl space-y-4 p-4 sm:p-6">
        <Button variant="ghost" size="sm" asChild>
          <Link href="/speaking/drills">
            <ArrowLeft className="mr-1.5 h-3.5 w-3.5" aria-hidden />
            Back to drills
          </Link>
        </Button>

        {loading ? (
          <Card className="flex min-h-[280px] items-center justify-center p-6 text-sm text-muted">
            <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden />
            Preparing your drill...
          </Card>
        ) : error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : drill && attempt ? (
          <>
            {feedback ? (
              <InlineAlert variant="success">
                Drill scored. You can re-record below or return to the catalogue for another target.
              </InlineAlert>
            ) : null}
            <DrillPlayer
              drill={drill}
              attemptId={attempt.attemptId}
              onComplete={setFeedback}
            />
          </>
        ) : (
          <InlineAlert variant="error">Could not start this speaking drill.</InlineAlert>
        )}
      </div>
    </LearnerDashboardShell>
  );
}