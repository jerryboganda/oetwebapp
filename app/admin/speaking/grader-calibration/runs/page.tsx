'use client';

/**
 * Admin · Speaking · Grader calibration · runs (owner request 7 Oct 2026).
 *
 * The recent harness runs, newest first, each opening the grade-by-grade AI-vs-expert comparison. Runs are started by
 * `scripts/speaking/grader-calibration.mjs` or its workflow (`speaking-grader-calibration`); this page only reads them.
 * It shows AI results, so it is kept apart from the blind marking pages.
 */
import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Scale } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { adminListGraderCalibrationRuns, type GraderCalibrationRunView } from '@/lib/api/speaking-grader-calibration';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Grader calibration', href: '/admin/speaking/grader-calibration' },
  { label: 'Runs' },
];

export default function SpeakingGraderCalibrationRunsPage() {
  const [runs, setRuns] = useState<GraderCalibrationRunView[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    adminListGraderCalibrationRuns()
      .then((next) => {
        if (!cancelled) setRuns(next);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load the runs.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <AdminTableLayout
      title="Grader calibration runs"
      description="The AI grader compared with your marks. A pilot is informational and can never pass; the score stays Provisional until a full validation run does. Do not re-mark a performance after reading its AI result: that anchors your marks."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
      banner={error ? <InlineAlert variant="error">{error}</InlineAlert> : undefined}
    >
      {!runs ? (
        error ? (
          <div className="p-8 text-center text-sm text-admin-fg-muted">The runs could not be loaded.</div>
        ) : (
          <div className="p-6"><Skeleton className="h-48 w-full rounded-admin-lg" /></div>
        )
      ) : runs.length === 0 ? (
        <div className="p-8 text-center text-sm text-admin-fg-muted">
          No run yet. Start one with the speaking-grader-calibration workflow (scope mock, pilot on, audio on).
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th scope="col" className="p-3">Started</th>
                <th scope="col" className="p-3">Graded</th>
                <th scope="col" className="p-3">Kind</th>
                <th scope="col" className="p-3">Status</th>
                <th scope="col" className="p-3">Progress</th>
                <th scope="col" className="p-3">Grader version</th>
                <th scope="col" className="p-3"><span className="sr-only">Action</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-admin-border">
              {runs.map((run) => (
                <tr key={run.id} data-testid="calibration-run-row">
                  <td className="p-3 tabular-nums">{new Date(run.createdAt).toLocaleString()}</td>
                  <td className="p-3">{run.scope === 'mock' ? 'Full Mocks' : 'Single cards'}</td>
                  <td className="p-3">{run.pilot ? <Badge variant="warning">Pilot</Badge> : <Badge variant="secondary">Validation</Badge>}</td>
                  <td className="p-3"><Badge variant={run.status === 'complete' ? 'success' : 'info'}>{run.status}</Badge></td>
                  <td className="p-3 tabular-nums">
                    {run.progress.done}/{run.progress.total}{run.progress.failed > 0 ? `, ${run.progress.failed} failed` : ''}
                  </td>
                  <td className="p-3"><code className="text-xs">{run.graderVersion || '-'}</code></td>
                  <td className="p-3 text-right">
                    <Button asChild size="sm" variant="outline">
                      <Link href={`/admin/speaking/grader-calibration/runs/${encodeURIComponent(run.id)}`}>Open</Link>
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </AdminTableLayout>
  );
}
