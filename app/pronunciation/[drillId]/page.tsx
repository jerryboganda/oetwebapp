'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, Headphones, Mic } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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

  return (
    <LearnerDashboardShell>
      <Link href="/pronunciation" className="mb-4 inline-flex items-center gap-2 text-sm font-medium text-muted hover:text-navy">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation
      </Link>

      {!drillId ? (
        <InlineAlert variant="warning">Missing pronunciation drill id.</InlineAlert>
      ) : loading ? (
        <div className="space-y-4">
          <Skeleton className="h-10 w-64" />
          <Skeleton className="h-48 rounded-xl" />
        </div>
      ) : error || !drill ? (
        <InlineAlert variant="warning">{error ?? 'Pronunciation drill not found.'}</InlineAlert>
      ) : (
        <div className="space-y-5">
          <Card className="p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="mb-2 flex flex-wrap gap-2">
                  <Badge variant="outline">{drill.difficulty}</Badge>
                  <Badge variant="muted">{drill.focus}</Badge>
                </div>
                <h1 className="text-2xl font-bold text-navy">{drill.label}</h1>
                <p className="mt-1 text-sm text-muted">Target phoneme: {drill.targetPhoneme}</p>
              </div>
              <Mic className="h-8 w-8 text-primary" />
            </div>
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card className="p-5">
              <h2 className="text-sm font-semibold text-navy">Example words</h2>
              <div className="mt-3 flex flex-wrap gap-2">
                {words.length > 0 ? words.map((word) => <Badge key={word} variant="outline">{word}</Badge>) : <p className="text-sm text-muted">No example words are published for this drill yet.</p>}
              </div>
            </Card>
            <Card className="p-5">
              <h2 className="text-sm font-semibold text-navy">Practice sentences</h2>
              <div className="mt-3 space-y-2">
                {sentences.length > 0 ? sentences.map((sentence) => <p key={sentence} className="text-sm text-muted">{sentence}</p>) : <p className="text-sm text-muted">No practice sentences are published for this drill yet.</p>}
              </div>
            </Card>
          </div>

          <Card className="p-5">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <h2 className="text-sm font-semibold text-navy">Minimal-pair discrimination</h2>
                <p className="mt-1 text-sm text-muted">Train your ear before recording a scored attempt.</p>
              </div>
              <Link href={`/pronunciation/discrimination/${encodeURIComponent(drill.id)}`}>
                <Button variant="outline"><Headphones className="h-4 w-4" /> Open discrimination</Button>
              </Link>
            </div>
          </Card>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
