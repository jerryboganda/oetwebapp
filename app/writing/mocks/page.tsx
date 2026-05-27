'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Award, PlayCircle, Clock, AlertTriangle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { listWritingMocks, startWritingMock } from '@/lib/writing/api';
import type { WritingMockDto } from '@/lib/writing/types';

export default function WritingMocksCataloguePage() {
  const router = useRouter();
  const [mocks, setMocks] = useState<WritingMockDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [starting, setStarting] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void listWritingMocks()
      .then((r) => {
        if (cancelled) return;
        setMocks(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load mocks.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const start = async (mockId: string) => {
    setStarting(mockId);
    setError(null);
    try {
      const session = await startWritingMock({ mockId });
      router.push(`/writing/mocks/session/${encodeURIComponent(session.id)}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start the mock.');
      setStarting(null);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Writing Mocks">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Exam simulation"
          icon={Award}
          accent="amber"
          title="Mocks under strict exam conditions"
          description="No coach, no spell-check, no pause. A mock is the single best predictor of your exam-day band."
          highlights={[
            { icon: Award, label: 'Available', value: `${mocks.length}` },
            { icon: Clock, label: 'Duration', value: '50 min' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card padding="md" className="border-amber-300/70 bg-amber-50/40">
          <CardContent>
            <p className="flex items-start gap-2 text-sm text-amber-900">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>
                <span className="font-bold">Before you start:</span> mocks reflect real exam conditions. Find a quiet 50-minute slot. Closing the browser mid-mock counts as abandoned.
              </span>
            </p>
          </CardContent>
        </Card>

        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label="Mock catalogue">
          {mocks.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">No mocks available yet.</p></li>
          ) : null}
          {mocks.map((mock) => (
            <li key={mock.id}>
              <Card padding="md" aria-label={`Mock: ${mock.title}`}>
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-2">
                    <Badge variant="info" size="sm">Mock</Badge>
                    <Badge variant={mock.status === 'published' ? 'success' : 'muted'} size="sm">{mock.status}</Badge>
                  </header>
                  <h2 className="mt-2 text-base font-bold text-navy">{mock.title}</h2>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Button
                      onClick={() => void start(mock.id)}
                      loading={starting === mock.id}
                      disabled={mock.status !== 'published'}
                      size="sm"
                    >
                      <PlayCircle className="h-4 w-4" aria-hidden="true" /> Take this mock
                    </Button>
                    <Button asChild variant="outline" size="sm">
                      <Link href="/writing/stats">See readiness</Link>
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
