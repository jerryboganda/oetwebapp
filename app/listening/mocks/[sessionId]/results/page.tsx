'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';

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
    fetch(`/v1/listening-pathway/mocks/sessions/${sessionId}/results`)
      .then((res) => (res.ok ? res.json() : null))
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
        <p className="text-slate-500">Loading your mock results…</p>
      </main>
    );
  }

  if (!result) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold">Mock results not yet available</h1>
        <p className="text-slate-600">
          This mock session is being graded. Refresh in a moment, or come back from the dashboard.
        </p>
        <Link href="/listening" className="rounded-md bg-slate-900 px-4 py-2 text-white text-sm inline-block">
          Back to dashboard
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <header className="rounded-2xl border border-slate-200 bg-white p-8 shadow-sm text-center">
        <h1 className="text-3xl font-bold">{result.scaledScore}</h1>
        <p className="text-sm text-slate-500 mt-1">
          Scaled OET Listening • Grade {result.gradeLabel}
        </p>
        <p className="mt-2 text-slate-700">
          Raw: <span className="font-mono">{result.rawScore} / 42</span>
        </p>
      </header>
      <nav className="flex flex-wrap gap-3 text-sm">
        <Link href="/listening" className="rounded-md bg-slate-900 px-4 py-2 text-white">
          Back to dashboard
        </Link>
        <Link
          href="/listening/stats"
          className="rounded-md border border-slate-300 px-4 py-2 text-slate-700"
        >
          See full analytics
        </Link>
      </nav>
    </main>
  );
}
