'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Headphones } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';

interface MockResult {
  sessionId: string;
  rawScore: number;
  scaledScore: number;
  gradeLabel: string;
}

export default function ListeningMockResultsPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId ?? '';
  const [result, setResult] = useState<MockResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient.get<MockResult | null>(`/v1/listening-pathway/mocks/sessions/${encodeURIComponent(sessionId)}/results`)
      .then((r) => {
        if (!cancelled) {
          setResult(r);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  if (loading) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12">
        <p className="text-muted">Loading your mock results…</p>
      </main>
    );
  }

  if (!result) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold text-navy">Mock results not yet available</h1>
        <p className="text-muted">
          This mock session is being graded. Refresh in a moment, or come back from the dashboard.
        </p>
        <Link href="/listening" className="rounded-md bg-primary px-4 py-2 text-white text-sm inline-block transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600">
          Back to dashboard
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <ResultsScorePanel
        eyebrow="Listening mock"
        icon={Headphones}
        title="Mock result"
        subtitle={`Scaled OET Listening · Grade ${result.gradeLabel}`}
        gaugeValue={(result.scaledScore / 500) * 100}
        gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{result.gradeLabel}</span>}
        gaugeLabel={`${result.scaledScore}/500`}
        gaugeColor={
          result.gradeLabel === 'A' || result.gradeLabel === 'B'
            ? 'var(--color-success)'
            : result.gradeLabel === 'C'
              ? 'var(--color-warning)'
              : 'var(--color-danger)'
        }
        grade={{
          label: `Grade ${result.gradeLabel}`,
          tone: result.gradeLabel === 'A' || result.gradeLabel === 'B' ? 'success' : result.gradeLabel === 'C' ? 'warning' : 'danger',
        }}
        stats={[
          { label: 'Scaled', value: `${result.scaledScore}/500`, tone: 'info' },
          { label: 'Raw score', value: `${result.rawScore}/42`, tone: 'default' },
          {
            label: 'Grade',
            value: result.gradeLabel,
            tone: result.gradeLabel === 'A' || result.gradeLabel === 'B' ? 'success' : result.gradeLabel === 'C' ? 'warning' : 'danger',
          },
        ]}
      />
      <nav className="flex flex-wrap gap-3 text-sm">
        <Link href="/listening" className="rounded-md bg-primary px-4 py-2 text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600">
          Back to dashboard
        </Link>
        <Link
          href="/listening/stats"
          className="rounded-md border border-border px-4 py-2 text-navy transition-colors hover:bg-background-light"
        >
          See full analytics
        </Link>
      </nav>
    </main>
  );
}
