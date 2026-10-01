'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, Headphones, Mic } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
import { fetchPronunciationDrill, type PronunciationDrillSummary } from '@/lib/api';
import { analytics } from '@/lib/analytics';

function firstParam(value: string | string[] | undefined): string | null {
  if (Array.isArray(value)) return value[0] ?? null;
  return value ?? null;
}

function parseJsonList(value: string): string[] {
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

export default function PronunciationDrillPage() {
  const params = useParams();
  const drillId = firstParam(params?.drillId);
  const [drill, setDrill] = useState<PronunciationDrillSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const words = useMemo(() => parseJsonList(drill?.exampleWordsJson ?? '[]'), [drill?.exampleWordsJson]);
  const sentences = useMemo(() => parseJsonList(drill?.sentencesJson ?? '[]'), [drill?.sentencesJson]);

  useEffect(() => {
    if (!drillId) {
      return;
    }

    let cancelled = false;
    analytics.track('pronunciation_drill_viewed', { drillId });
    (fetchPronunciationDrill(drillId) as Promise<PronunciationDrillSummary>)
      .then((result) => {
        if (!cancelled) setDrill(result);
      })
      .catch(() => {
        if (!cancelled) setError('Could not load this pronunciation drill.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [drillId]);

  if (!drillId || (!loading && (error || !drill))) {
    return (
      <EmptyState
        icon={<Mic className="h-7 w-7" aria-hidden="true" />}
        title={!drillId ? 'Missing pronunciation drill id.' : (error ?? 'Pronunciation drill not found.')}
        action={{ label: 'Back to pronunciation', href: '/pronunciation' }}
      />
    );
  }

  if (loading || !drill) {
    return (
      <>
        <Skeleton className="h-36 rounded-2xl" />
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-40 rounded-2xl" />
        </div>
      </>
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow={`${drill.difficulty} · ${drill.focus}`}
        title={drill.label}
        description={`Target phoneme: ${drill.targetPhoneme}`}
        icon={Mic}
        aside={(
          <Button variant="ghost" size="sm" asChild>
            <Link href="/pronunciation">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to pronunciation
            </Link>
          </Button>
        )}
      />

      <MotionSection className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card padding="lg">
          <h2 className="text-base font-bold text-navy">Example words</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            {words.length > 0 ? words.map((word) => <Badge key={word} variant="outline">{word}</Badge>) : <p className="text-sm text-muted">No example words are published for this drill yet.</p>}
          </div>
        </Card>
        <Card padding="lg">
          <h2 className="text-base font-bold text-navy">Practice sentences</h2>
          <div className="mt-3 space-y-2">
            {sentences.length > 0 ? sentences.map((sentence) => <p key={sentence} className="text-sm text-muted">{sentence}</p>) : <p className="text-sm text-muted">No practice sentences are published for this drill yet.</p>}
          </div>
        </Card>
      </MotionSection>

      <MotionSection>
        <Card padding="lg">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="text-base font-bold text-navy">Minimal-pair discrimination</h2>
              <p className="mt-1 text-sm text-muted">Train your ear before recording a scored attempt.</p>
            </div>
            <Button variant="outline" asChild>
              <Link href={`/pronunciation/discrimination/${encodeURIComponent(drill.id)}`}>
                <Headphones className="h-4 w-4" aria-hidden="true" /> Open discrimination
              </Link>
            </Button>
          </div>
        </Card>
      </MotionSection>
    </>
  );
}
