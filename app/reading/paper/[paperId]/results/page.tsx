'use client';

import { use, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { OetStatementOfResultsCard, type OetStatementOfResults } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { getReadingAttempt } from '@/lib/reading-authoring-api';

export default function ReadingPaperResultsPage({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const search = useSearchParams();
  const attemptId = search?.get('attemptId') ?? '';

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sor, setSor] = useState<OetStatementOfResults | null>(null);

  useEffect(() => {
    if (!attemptId) {
      setError('Missing attemptId');
      setLoading(false);
      return;
    }
    (async () => {
      try {
        const a = await getReadingAttempt(attemptId);
        setSor({
          candidate: { name: 'Practice Candidate', candidateNumber: `OET-${attemptId.slice(0, 6).toUpperCase()}-${attemptId.slice(-6).toUpperCase()}` },
          venue: { name: 'OET Prep — Practice Mock', number: `PREP-${paperId.slice(0, 6).toUpperCase()}`, country: 'United Kingdom' },
          test: { date: new Date(a.submittedAt ?? a.startedAt).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }), deliveryMode: 'OET on computer (practice)', profession: 'Medicine' },
          scores: {
            // Only Reading was graded on this attempt; others shown as 0 until
            // the learner completes those subtests. The SoR format still works.
            listening: 0,
            reading: a.scaledScore ?? 0,
            speaking: 0,
            writing: 0,
          },
          isPractice: true,
          issuedAt: new Date().toISOString(),
        });
      } catch (e) {
        setError((e as Error).message);
      } finally {
        setLoading(false);
      }
    })();
  }, [attemptId, paperId]);

  return (
    <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
      <Link href={`/reading/paper/${paperId}`} className="inline-flex items-center gap-2 text-sm text-muted hover:text-navy">
        <ArrowLeft className="w-4 h-4" /> Back to paper
      </Link>

      {loading && <Skeleton className="h-96 mt-4" />}
      {error && <InlineAlert variant="error">{error}</InlineAlert>}
      {sor && (
        <div className="mt-6 space-y-4">
          <Badge variant="info">Reading — standalone attempt</Badge>
          <OetStatementOfResultsCard data={sor} />
          <p className="text-xs text-muted text-center">
            Listening / Writing / Speaking are shown as zero until you complete a full mock. Take a
            full mock exam from <Link href="/mocks" className="underline">Mocks</Link> for a
            complete Statement of Results.
          </p>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
