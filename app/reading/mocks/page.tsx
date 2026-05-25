'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Clock, FileText, ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { startMock } from '@/lib/reading-pathway-api';

interface MockTemplate {
  id: string;
  title: string;
  durationMinutes: number;
  parts: string;
}

const MOCK_TEMPLATES: MockTemplate[] = [
  { id: 'mock-template-1', title: 'Mock Test 1', durationMinutes: 60, parts: 'Parts A, B, C' },
  { id: 'mock-template-2', title: 'Mock Test 2', durationMinutes: 60, parts: 'Parts A, B, C' },
  { id: 'mock-template-3', title: 'Mock Test 3', durationMinutes: 60, parts: 'Parts A, B, C' },
];

export default function ReadingMocksPage() {
  const router = useRouter();
  const [starting, setStarting] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleStartMock(templateId: string) {
    setStarting(templateId);
    setError(null);
    try {
      const session = await startMock(templateId);
      router.push(`/reading/mocks/${encodeURIComponent(session.sessionId)}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start mock. Please try again.');
      setStarting(null);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Reading Mocks">
      <main className="space-y-8">
        <Link
          href="/reading"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy dark:hover:text-white"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Reading
        </Link>

        <div>
          <h1 className="text-2xl font-bold text-navy dark:text-white">Reading Mock Tests</h1>
          <p className="mt-1 text-sm text-muted">
            Simulate full OET Reading exam conditions with timed mock tests.
          </p>
        </div>

        {error ? (
          <div className="rounded-lg border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20 px-4 py-3 text-sm text-red-700 dark:text-red-300">
            {error}
          </div>
        ) : null}

        {/* Mock catalog */}
        <section>
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-muted">Available Mocks</h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            {MOCK_TEMPLATES.map((mock) => (
              <div
                key={mock.id}
                className="rounded-xl border border-border bg-surface p-5 flex flex-col gap-4"
              >
                <div>
                  <div className="flex items-center gap-2 mb-1">
                    <FileText className="h-4 w-4 text-blue-500" aria-hidden />
                    <h3 className="text-sm font-semibold text-navy dark:text-white">{mock.title}</h3>
                  </div>
                  <p className="text-xs text-muted">Full Paper — {mock.parts}</p>
                </div>
                <div className="flex items-center gap-1.5 text-xs text-muted">
                  <Clock className="h-3.5 w-3.5" aria-hidden />
                  {mock.durationMinutes} minutes
                </div>
                <button
                  type="button"
                  disabled={starting !== null}
                  onClick={() => handleStartMock(mock.id)}
                  className="mt-auto rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                >
                  {starting === mock.id ? 'Starting…' : 'Start Mock'}
                </button>
              </div>
            ))}
          </div>
        </section>

        {/* Past results — empty state */}
        <section>
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-muted">Past Attempts</h2>
          <div className="rounded-xl border border-border bg-surface px-6 py-10 text-center">
            <FileText className="mx-auto h-8 w-8 text-muted mb-3" aria-hidden />
            <p className="text-sm font-medium text-navy dark:text-white">No mock attempts yet</p>
            <p className="mt-1 text-xs text-muted">
              Complete a mock above to see your results here.
            </p>
          </div>
        </section>
      </main>
    </LearnerDashboardShell>
  );
}
